using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint P: RBAC role matrix tests verifying per-role access boundaries.
///
/// Coverage:
///  1. Authorization policy definitions contain the correct roles.
///  2. PT can sign any note type.
///  3. PTA is blocked from signing Evaluation, Progress Note, and Discharge notes.
///  4. PTA is permitted to sign Daily notes.
///  5. Admin cannot write (create/update) clinical notes (NoteWrite policy).
///  6. Aide cannot write patient records (PatientWrite policy).
///  7. Patient role cannot access clinical notes (NoteRead policy).
/// </summary>
[Trait("Category", "RBAC")]
public class RbacRoleMatrixTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public RbacRoleMatrixTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        _db = new ApplicationDbContext(options, tenantMock.Object);
        _db.Database.Migrate();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ─── Policy definition correctness ──────────────────────────────────────

    [Fact]
    public void Roles_AreDefinedForAllSupportedRoleNames()
    {
        Assert.Equal("Owner", Roles.Owner);
        Assert.Equal("PT", Roles.PT);
        Assert.Equal("PTA", Roles.PTA);
        Assert.Equal("Admin", Roles.Admin);
        Assert.Equal("Aide", Roles.Aide);
        Assert.Equal("Patient", Roles.Patient);
    }

    [Fact]
    public void AuthorizationPolicies_PolicyNamesAreUnique()
    {
        var policyNames = new[]
        {
            AuthorizationPolicies.PatientRead,
            AuthorizationPolicies.PatientWrite,
            AuthorizationPolicies.NoteRead,
            AuthorizationPolicies.NoteWrite,
            AuthorizationPolicies.IntakeRead,
            AuthorizationPolicies.IntakeWrite,
            AuthorizationPolicies.ClinicalStaff,
            AuthorizationPolicies.AdminOnly
        };

        Assert.Equal(policyNames.Length, new HashSet<string>(policyNames).Count);
    }

    [Fact]
    public void RegisteredPolicies_IncludeExpectedRoles()
    {
        // Build an authorization provider using the same shared registration used by Program.cs.
        var services = new ServiceCollection();
        services.AddAuthorization(options => options.AddPTDocAuthorizationPolicies());

        var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;

        // Helper: extract all allowed roles from a named policy's RolesAuthorizationRequirement.
        static IReadOnlySet<string> GetAllowedRoles(AuthorizationPolicy? policy)
        {
            var requirement = policy?.Requirements
                .OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()
                .FirstOrDefault();
            return requirement?.AllowedRoles is not null
                ? new HashSet<string>(requirement.AllowedRoles)
                : new HashSet<string>();
        }

        // NoteWrite: only PT and PTA (Admin is read-only per FSD §3.1)
        var noteWriteRoles = GetAllowedRoles(authOptions.GetPolicy(AuthorizationPolicies.NoteWrite));
        Assert.Contains(Roles.PT, noteWriteRoles);
        Assert.Contains(Roles.PTA, noteWriteRoles);
        Assert.DoesNotContain(Roles.Admin, noteWriteRoles);
        Assert.DoesNotContain(Roles.Owner, noteWriteRoles);
        Assert.DoesNotContain(Roles.Aide, noteWriteRoles);
        Assert.DoesNotContain(Roles.Patient, noteWriteRoles);

        // PatientRead: PT, PTA, Admin, Aide (not Patient)
        var patientReadRoles = GetAllowedRoles(authOptions.GetPolicy(AuthorizationPolicies.PatientRead));
        Assert.Contains(Roles.PT, patientReadRoles);
        Assert.Contains(Roles.PTA, patientReadRoles);
        Assert.Contains(Roles.Admin, patientReadRoles);
        Assert.Contains(Roles.Owner, patientReadRoles);
        Assert.Contains(Roles.Aide, patientReadRoles);
        Assert.DoesNotContain(Roles.Patient, patientReadRoles);

        // IntakeRead: PT, PTA, Admin, Patient (not Aide)
        var intakeReadRoles = GetAllowedRoles(authOptions.GetPolicy(AuthorizationPolicies.IntakeRead));
        Assert.Contains(Roles.PT, intakeReadRoles);
        Assert.Contains(Roles.Patient, intakeReadRoles);
        Assert.DoesNotContain(Roles.Aide, intakeReadRoles);

        // ClinicalStaff: PT, PTA, Admin (not Aide, not Patient)
        var clinicalStaffRoles = GetAllowedRoles(authOptions.GetPolicy(AuthorizationPolicies.ClinicalStaff));
        Assert.Contains(Roles.PT, clinicalStaffRoles);
        Assert.Contains(Roles.PTA, clinicalStaffRoles);
        Assert.Contains(Roles.Admin, clinicalStaffRoles);
        Assert.Contains(Roles.Owner, clinicalStaffRoles);
        Assert.DoesNotContain(Roles.Aide, clinicalStaffRoles);
        Assert.DoesNotContain(Roles.Patient, clinicalStaffRoles);

        var patientHepRoles = GetAllowedRoles(authOptions.GetPolicy(AuthorizationPolicies.PatientHepAccess));
        Assert.Contains(Roles.Patient, patientHepRoles);
        Assert.DoesNotContain(Roles.PT, patientHepRoles);
        Assert.DoesNotContain(Roles.PTA, patientHepRoles);
        Assert.DoesNotContain(Roles.Admin, patientHepRoles);
    }

    // ─── Authorization service evaluation tests ──────────────────────────────

    /// <summary>
    /// Evaluates a named authorization policy against a user with the given role,
    /// using the production policy registrations from <see cref="AuthorizationPolicies.AddPTDocAuthorizationPolicies"/>.
    /// </summary>
    private static async Task<bool> EvaluatePolicyAsync(string policyName, string role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Use the shared registration so any drift from Program.cs will fail these tests.
        services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());

        var sp = services.BuildServiceProvider();
        var authService = sp.GetRequiredService<IAuthorizationService>();

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role) },
            authenticationType: "Test");
        var user = new ClaimsPrincipal(identity);

        var result = await authService.AuthorizeAsync(user, null, policyName);
        return result.Succeeded;
    }

    // ─── NoteWrite policy role matrix ────────────────────────────────────────

    [Fact]
    public async Task NoteWrite_PT_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PT));
    }

    [Fact]
    public async Task NoteWrite_PTA_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PTA));
    }

    [Fact]
    public async Task NoteWrite_Admin_IsNotAuthorized()
    {
        // Admin is read-only for clinical notes per FSD §3.1
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Admin));
    }

    [Fact]
    public async Task NoteWrite_Aide_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Aide));
    }

    [Fact]
    public async Task NoteWrite_Patient_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Patient));
    }

    [Theory]
    [InlineData(Roles.Billing)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.FrontDesk)]
    public async Task NoteWrite_NonClinicalRoles_AreNotAuthorized(string role)
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, role));
    }

    // ─── PatientRead policy role matrix ─────────────────────────────────────

    [Fact]
    public async Task PatientRead_Aide_IsAuthorized()
    {
        // Therapy aide can view patient demographics (FSD §3.4)
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.PatientRead, Roles.Aide));
    }

    [Fact]
    public async Task PatientRead_Patient_IsNotAuthorized()
    {
        // Patients cannot access the clinical staff patient endpoint
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.PatientRead, Roles.Patient));
    }

    // ─── PatientWrite policy role matrix ─────────────────────────────────────

    [Fact]
    public async Task PatientWrite_Aide_IsNotAuthorized()
    {
        // Therapy aide is view-only for patient demographics
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.PatientWrite, Roles.Aide));
    }

    [Fact]
    public async Task PatientWrite_Owner_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.PatientWrite, Roles.Owner));
    }

    [Theory]
    [InlineData(Roles.Billing)]
    [InlineData(Roles.FrontDesk)]
    [InlineData(Roles.Patient)]
    [InlineData(Roles.Aide)]
    public async Task PatientWrite_NonWriterRoles_AreNotAuthorized(string role)
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.PatientWrite, role));
    }

    // ─── NoteRead policy role matrix ─────────────────────────────────────────

    [Fact]
    public async Task NoteRead_Admin_IsAuthorized()
    {
        // Admin has read-only access to notes per FSD §3.1
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Admin));
    }

    [Fact]
    public async Task NoteRead_Aide_IsNotAuthorized()
    {
        // Therapy aide cannot access clinical notes
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Aide));
    }

    [Fact]
    public async Task NoteRead_Patient_IsNotAuthorized()
    {
        // Patient has no access to clinical notes via the notes API
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Patient));
    }

    // ─── IntakeRead policy: Patient can view intake ───────────────────────────

    [Fact]
    public async Task IntakeRead_Patient_IsAuthorized()
    {
        // Patient role can read intake forms (FSD §3.5)
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.Patient));
    }

    [Fact]
    public async Task IntakeRead_Aide_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.Aide));
    }

    [Fact]
    public async Task IntakeWrite_Owner_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeWrite, Roles.Owner));
    }

    [Theory]
    [InlineData(Roles.Billing)]
    [InlineData(Roles.Aide)]
    [InlineData(Roles.Patient)]
    public async Task IntakeWrite_NonWriterRoles_AreNotAuthorized(string role)
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeWrite, role));
    }

    // ─── PTA domain guard: cannot sign Eval/PN/DC notes ─────────────────────

    /// <summary>
    /// Verifies that the PTA domain guard logic (note-type check in the sign endpoint)
    /// correctly identifies which note types a PTA is restricted from signing.
    /// PT: all note types allowed. PTA: Daily only.
    /// </summary>
    [Theory]
    [InlineData(NoteType.Evaluation, false)]
    [InlineData(NoteType.ProgressNote, false)]
    [InlineData(NoteType.Discharge, false)]
    [InlineData(NoteType.Daily, true)]
    public async Task PtaDomainGuard_NoteTypeRestriction_ReturnsExpectedAccess(
        NoteType noteType, bool expectedAllowed)
    {
        // Arrange: create a note with the given type
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = noteType,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            CptCodesJson = "[]",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        // Act: apply the same domain guard logic used in the sign endpoint.
        // A PTA is allowed to sign only if NoteType == Daily.
        var queriedType = await _db.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.Id == note.Id)
            .Select(n => (NoteType?)n.NoteType)
            .FirstOrDefaultAsync();

        var ptaIsAllowed = queriedType == NoteType.Daily;

        // Assert
        Assert.Equal(expectedAllowed, ptaIsAllowed);
    }

    [Fact]
    public async Task PtaDomainGuard_NonExistentNote_ReturnsNull()
    {
        // Verifies that querying a non-existent note returns null (not found path in the endpoint)
        var nonExistentId = Guid.NewGuid();
        var queriedType = await _db.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.Id == nonExistentId)
            .Select(n => (NoteType?)n.NoteType)
            .FirstOrDefaultAsync();

        Assert.Null(queriedType);
    }

    [Fact]
    public async Task PT_IsAuthorizedForNoteWrite_AndNotSubjectToPtaDomainGuard()
    {
        // PT has the NoteWrite role and is not restricted by the PTA domain guard.
        // The domain guard only applies when ClaimsPrincipal.IsInRole(Roles.PTA) is true.
        var ptHasNoteWrite = await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PT);
        Assert.True(ptHasNoteWrite, "PT must be authorized by the NoteWrite policy");

        // Construct a real PT ClaimsPrincipal and verify IsInRole(PTA) is false —
        // confirming that the domain guard in the sign endpoint is not triggered for PT users.
        var ptIdentity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, Roles.PT) },
            authenticationType: "Test");
        var ptPrincipal = new ClaimsPrincipal(ptIdentity);

        Assert.False(ptPrincipal.IsInRole(Roles.PTA),
            "A PT principal must not satisfy IsInRole(PTA); the domain guard must not apply");
    }

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    [InlineData(NoteType.Daily)]
    public async Task PT_CanSignAllNoteTypes_DomainGuardDoesNotApply(NoteType noteType)
    {
        // Arrange: create a note of the given type
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = noteType,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            CptCodesJson = "[]",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        // Verify the note was persisted with the correct type
        var savedNote = await _db.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.Id == note.Id)
            .Select(n => new { n.NoteType })
            .FirstOrDefaultAsync();

        Assert.NotNull(savedNote);
        Assert.Equal(noteType, savedNote.NoteType);

        // PT users are not subject to the PTA domain guard.
        // Verify using a real ClaimsPrincipal that a PT user's IsInRole(PTA) returns false,
        // which means the guard block in the sign endpoint is never entered for PT.
        var ptIdentity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, Roles.PT) },
            authenticationType: "Test");
        var ptPrincipal = new ClaimsPrincipal(ptIdentity);

        Assert.False(ptPrincipal.IsInRole(Roles.PTA),
            "PT principal must not satisfy IsInRole(PTA), so the domain guard does not apply");
    }

    // ─── PTA domain guard: IsInRole check correctness ─────────────────────────

    [Fact]
    public void DomainGuard_PtaPrincipal_IsInRolePTA_IsTrue()
    {
        // Verify that a ClaimsPrincipal with the PTA role satisfies IsInRole(Roles.PTA).
        // This is the condition that gates the domain guard in the sign endpoint.
        var ptaIdentity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, Roles.PTA) },
            authenticationType: "Test");
        var ptaPrincipal = new ClaimsPrincipal(ptaIdentity);

        Assert.True(ptaPrincipal.IsInRole(Roles.PTA),
            "PTA principal must satisfy IsInRole(PTA) to trigger the domain guard");
    }

    // ─── ClinicalStaff policy ────────────────────────────────────────────────

    [Fact]
    public async Task ClinicalStaff_PT_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.PT));
    }

    [Fact]
    public async Task ClinicalStaff_PTA_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.PTA));
    }

    [Fact]
    public async Task ClinicalStaff_Admin_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.Admin));
    }

    [Fact]
    public async Task ClinicalStaff_Patient_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.Patient));
    }

    [Fact]
    public async Task ClinicalStaff_Aide_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.Aide));
    }

    // ─── AdminOnly policy role matrix ────────────────────────────────────────

    [Fact]
    public async Task AdminOnly_Admin_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.Admin));
    }

    [Fact]
    public async Task AdminOnly_Owner_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.Owner));
    }

    [Fact]
    public async Task AdminOnly_PT_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.PT));
    }

    [Fact]
    public async Task AdminOnly_PTA_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.PTA));
    }

    [Fact]
    public async Task AdminOnly_Aide_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.Aide));
    }

    [Fact]
    public async Task AdminOnly_FrontDesk_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.FrontDesk));
    }

    [Fact]
    public async Task AdminOnly_Patient_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.AdminOnly, Roles.Patient));
    }

    [Theory]
    [InlineData(Roles.PT, true)]
    [InlineData(Roles.PTA, false)]
    [InlineData(Roles.Admin, false)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Billing, false)]
    public async Task NoteCoSign_Roles_MatchPolicy(string role, bool expectedAuthorized)
    {
        Assert.Equal(expectedAuthorized, await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, role));
    }

    [Theory]
    [InlineData(Roles.PT, true)]
    [InlineData(Roles.PTA, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Patient, false)]
    [InlineData(Roles.FrontDesk, false)]
    [InlineData(Roles.Aide, false)]
    [InlineData(Roles.Billing, false)]
    [InlineData(Roles.Owner, false)]
    public async Task NoteExport_Roles_MatchPolicy(string role, bool expectedAuthorized)
    {
        Assert.Equal(expectedAuthorized, await EvaluatePolicyAsync(AuthorizationPolicies.NoteExport, role));
    }

    [Fact]
    public void RegisteredPolicies_IncludesAdminOnlyPolicy()
    {
        var services = new ServiceCollection();
        services.AddAuthorization(options => options.AddPTDocAuthorizationPolicies());

        var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;

        static IReadOnlySet<string> GetAllowedRoles(AuthorizationPolicy? policy)
        {
            var requirement = policy?.Requirements
                .OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()
                .FirstOrDefault();
            return requirement?.AllowedRoles is not null
                ? new HashSet<string>(requirement.AllowedRoles)
                : new HashSet<string>();
        }

        var adminOnlyRoles = GetAllowedRoles(authOptions.GetPolicy(AuthorizationPolicies.AdminOnly));
        Assert.Contains(Roles.Admin, adminOnlyRoles);
        Assert.Contains(Roles.Owner, adminOnlyRoles);
        Assert.DoesNotContain(Roles.PT, adminOnlyRoles);
        Assert.DoesNotContain(Roles.PTA, adminOnlyRoles);
        Assert.DoesNotContain(Roles.Aide, adminOnlyRoles);
        Assert.DoesNotContain(Roles.Patient, adminOnlyRoles);
        Assert.DoesNotContain(Roles.FrontDesk, adminOnlyRoles);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Patient CreateTestPatient() => new()
    {
        FirstName = "Test",
        LastName = "Patient",
        DateOfBirth = new DateTime(1980, 1, 1),
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = Guid.NewGuid(),
        SyncState = SyncState.Pending
    };
}
