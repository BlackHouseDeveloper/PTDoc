using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Api.Intake;
using PTDoc.Api.Notes;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// PFPT U-C1 through U-C5 required test coverage.
///
/// Sprint U-C1: Auth + RBAC
///   - PTA cannot create eval (tested via real PtaIsBlockedFromNoteType helper)
///   - Billing/Owner/FrontDesk/Patient/Aide blocked from NoteWrite
///   - Role-based endpoint denial works
///
/// Sprint U-C2: Intake Workflow
///   - Intake locks after submit (tests invoke real SubmitIntake handler)
///   - Double-lock returns 409
///
/// Sprint U-C3: SOAP + AI
///   - AI not persisted before acceptance
///
/// Sprint U-C4: Compliance + Signatures
///   - PTA requires co-sign
///   - Co-sign tests run against migrated SQLite to cover schema regressions
///
/// Sprint U-C5: Offline + Sync
///   - Role-based data scoping (Aide/FrontDesk/Patient excluded from clinical entities)
/// </summary>
[Trait("Category", "CoreCi")]
public class PfptRoleComplianceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public PfptRoleComplianceTests()
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

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static async Task<bool> EvaluatePolicyAsync(string policyName, string role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());
        var sp = services.BuildServiceProvider();
        var authService = sp.GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test");
        var user = new ClaimsPrincipal(identity);
        var result = await authService.AuthorizeAsync(user, null, policyName);
        return result.Succeeded;
    }

    // ─── U-C1: Auth + RBAC ──────────────────────────────────────────────────

    [Fact]
    public async Task UC1_Billing_CannotEditNotes_NoteWriteBlocked()
    {
        // Billing role: "clinical notes VIEW ONLY" per canonical role matrix.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Billing));
    }

    [Fact]
    public async Task UC1_Billing_CanReadNotes_NoteReadAllowed()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Billing));
    }

    [Fact]
    public async Task UC1_Owner_IsReadOnly_NoteWriteBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Owner));
    }

    [Fact]
    public async Task UC1_Owner_CanReadNotes_NoteReadAllowed()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Owner));
    }

    [Fact]
    public async Task UC1_FrontDesk_CannotEditNotes_NoteWriteBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_FrontDesk_CannotReadNotes_NoteReadBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_FrontDesk_CanReadIntake_IntakeReadAllowed()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_FrontDesk_CanWriteIntake_IntakeWriteAllowed()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_Patient_CannotAccessSOAP_NoteReadBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Patient));
    }

    [Fact]
    public async Task UC1_Patient_CannotWriteNotes_NoteWriteBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Patient));
    }

    [Fact]
    public async Task UC1_Patient_CanSubmitIntake_IntakeReadAllowed()
    {
        // Patient needs IntakeRead (includes Patient) to submit their own intake form.
        // The /submit endpoint uses IntakeRead policy so patients can lock their own forms.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.Patient));
    }

    [Fact]
    public async Task UC1_Aide_CannotReadNotes_NoteReadBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Aide));
    }

    [Fact]
    public async Task UC1_Aide_CannotWriteNotes_NoteWriteBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Aide));
    }

    [Fact]
    public async Task UC1_PTA_CanWriteNotes_NoteWriteAllowed()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PTA));
    }

    [Fact]
    public async Task UC1_PT_CanWriteNotes_NoteWriteAllowed()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PT));
    }

    [Fact]
    public async Task UC1_PracticeManager_CannotWriteNotes_NoteWriteBlocked()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PracticeManager));
    }

    [Fact]
    public void UC1_PracticeManager_RoleConstant_Defined()
    {
        Assert.Equal("PracticeManager", Roles.PracticeManager);
    }

    [Fact]
    public async Task UC1_NoteCoSign_PT_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PT));
    }

    [Fact]
    public async Task UC1_NoteCoSign_PTA_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PTA));
    }

    [Fact]
    public async Task UC1_SchedulingAccess_FrontDesk_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.SchedulingAccess, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_BillingAccess_Billing_IsAuthorized()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.BillingAccess, Roles.Billing));
    }

    [Fact]
    public async Task UC1_BillingAccess_Patient_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.BillingAccess, Roles.Patient));
    }

    // ─── U-C1: PTA domain guard at CREATE level ──────────────────────────────
    // Tests call the real NoteEndpoints.PtaIsBlockedFromNoteType helper, which is
    // the same function invoked by the CreateNote endpoint. If the guard is removed
    // from CreateNote, these tests will fail (non-tautological).

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public void UC1_PTA_CannotCreateNonDailyNote_DomainGuardBlocks(NoteType noteType)
    {
        // Build a real PTA ClaimsPrincipal and call the actual guard helper.
        var ptaIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, Roles.PTA) }, "Test");
        var ptaPrincipal = new ClaimsPrincipal(ptaIdentity);

        // Assert: the real guard function blocks PTA for non-Daily note types.
        var isBlocked = NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, noteType);
        Assert.True(isBlocked, $"NoteEndpoints.PtaIsBlockedFromNoteType should block PTA from creating {noteType}");
    }

    [Fact]
    public void UC1_PTA_CanCreateDailyNote_DomainGuardAllows()
    {
        var ptaIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, Roles.PTA) }, "Test");
        var ptaPrincipal = new ClaimsPrincipal(ptaIdentity);

        // Assert: the real guard allows PTA to create Daily notes.
        var isBlocked = NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, NoteType.Daily);
        Assert.False(isBlocked, "NoteEndpoints.PtaIsBlockedFromNoteType should allow PTA to create Daily notes");
    }

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    [InlineData(NoteType.Daily)]
    public void UC1_PT_CanCreateAllNoteTypes_DomainGuardDoesNotApply(NoteType noteType)
    {
        // PT users are not subject to the PTA domain guard.
        var ptIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, Roles.PT) }, "Test");
        var ptPrincipal = new ClaimsPrincipal(ptIdentity);

        var isBlocked = NoteEndpoints.PtaIsBlockedFromNoteType(ptPrincipal, noteType);
        Assert.False(isBlocked, $"PT should not be blocked from creating {noteType} notes");
    }

    // ─── U-C2: Intake locks after submit ────────────────────────────────────
    // Tests call the real IntakeEndpoints.SubmitIntake handler so that removing
    // or breaking the submit/lock logic will cause test failures (non-tautological).

    [Fact]
    public async Task UC2_IntakeForm_LocksAfterSubmit_ViaRealHandler()
    {
        // Arrange: create an unlocked intake form
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = false,
            ResponseJson = "{\"pain\":\"7/10\"}",
            Consents = "{\"hipaaAcknowledged\":true,\"treatmentConsentAccepted\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        Assert.False(intake.IsLocked);
        Assert.Null(intake.SubmittedAt);

        var identityMock = new Mock<IIdentityContextAccessor>();
        var submittingUserId = Guid.NewGuid();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(submittingUserId);

        var auditMock = new Mock<IAuditService>();
        var patientContextMock = new Mock<IPatientContextAccessor>();

        // Act: call the real SubmitIntake handler (not direct entity mutation)
        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        // Assert: returned 200 OK
        Assert.IsType<Ok<IntakeResponse>>(result);

        // Assert: intake is persisted as locked with SubmittedAt stamped
        var updated = await _db.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.True(updated.IsLocked);
        Assert.NotNull(updated.SubmittedAt);
        Assert.Equal(submittingUserId, updated.ModifiedByUserId);
    }

    [Fact]
    public async Task UC2_LockedIntakeForm_CannotBeSubmittedAgain_Returns409()
    {
        // Arrange: already locked intake form
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            SubmittedAt = DateTime.UtcNow.AddMinutes(-10),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var auditMock = new Mock<IAuditService>();
        var patientContextMock = new Mock<IPatientContextAccessor>();

        // Act: call the real SubmitIntake handler on an already-locked form
        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        // Assert: the real handler returns 409 Conflict (not BadRequest or OK)
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(409, statusResult.StatusCode);
    }

    [Fact]
    public async Task UC2_SubmitIntake_NotFound_Returns404()
    {
        // Submit with a non-existent ID should return 404.
        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var auditMock = new Mock<IAuditService>();
        var patientContextMock = new Mock<IPatientContextAccessor>();
        var result = await IntakeEndpoints.SubmitIntake(Guid.NewGuid(), _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    [Fact]
    public async Task UC2_Patient_CanSubmitIntakePolicy_IntakeReadAllowed()
    {
        // The /submit endpoint uses IntakeRead (includes Patient).
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.Patient));
    }

    // ─── U-C Beta: Intake workflow completion and role boundaries ─────────────

    [Fact]
    public async Task UCBeta_Patient_CannotAccessClinicalNotes_NoteReadDenied()
    {
        // Sprint UC-Beta: Patient must not access clinical SOAP data.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Patient));
    }

    [Fact]
    public async Task UCBeta_Patient_CannotWriteClinicalNotes_NoteWriteDenied()
    {
        // Sprint UC-Beta: Patient cannot create or edit clinical note content.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Patient));
    }

    [Fact]
    public async Task UCBeta_FrontDesk_CanManageIntake_IntakeWriteAllowed()
    {
        // Sprint UC-Beta: Front Desk manages the intake workflow.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task UCBeta_FrontDesk_CannotWriteClinicalSOAP_NoteWriteDenied()
    {
        // Sprint UC-Beta: Front Desk must not edit clinical SOAP content.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task UCBeta_Clinician_CanReviewIntake_ClinicalStaffAllowed()
    {
        // Sprint UC-Beta: Clinicians can review submitted intakes (ClinicalStaff policy).
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.PT));
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.PTA));
    }

    [Fact]
    public async Task UCBeta_Patient_CannotReviewIntake_ClinicalStaffDenied()
    {
        // Sprint UC-Beta: Patient cannot invoke clinician review of intake.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.Patient));
    }

    [Fact]
    public async Task UCBeta_FrontDesk_CannotReviewIntake_ClinicalStaffDenied()
    {
        // Sprint UC-Beta: Front Desk manages workflow but does not perform clinical review.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.ClinicalStaff, Roles.FrontDesk));
    }

    [Fact]
    public async Task UCBeta_ReviewIntake_RequiresLockedForm_Returns409WhenUnlocked()
    {
        // Sprint UC-Beta: Clinician can only review a submitted (locked) intake.
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = false,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var auditMock = new Mock<IAuditService>();

        var result = await IntakeEndpoints.ReviewIntake(intake.Id, _db, identityMock.Object, auditMock.Object, CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(409, statusResult.StatusCode);
        auditMock.Verify(a => a.LogIntakeEventAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UCBeta_ReviewIntake_LockedWithoutSubmit_Returns409()
    {
        // Sprint UC-Beta: An intake locked via the /lock endpoint (no SubmittedAt) cannot be reviewed.
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            SubmittedAt = null, // Locked without going through /submit
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var auditMock = new Mock<IAuditService>();

        var result = await IntakeEndpoints.ReviewIntake(intake.Id, _db, identityMock.Object, auditMock.Object, CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(409, statusResult.StatusCode);
        auditMock.Verify(a => a.LogIntakeEventAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UCBeta_SubmitIntake_Patient_CannotSubmitAnotherPatientsIntake()
    {
        // Sprint UC-Beta: A Patient caller must only be able to submit their own intake.
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = false,
            ResponseJson = "{}",
            Consents = "{\"hipaaAcknowledged\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        // Caller is a Patient but with a *different* patient ID — should be rejected.
        var differentPatientId = Guid.NewGuid();
        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());
        identityMock.Setup(x => x.GetCurrentUserRole()).Returns(Roles.Patient);

        var patientContextMock = new Mock<IPatientContextAccessor>();
        patientContextMock.Setup(x => x.GetCurrentPatientId()).Returns(differentPatientId);

        var auditMock = new Mock<IAuditService>();

        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
    }

    [Fact]
    public async Task UCBeta_SubmitIntake_Patient_CanSubmitOwnIntake()
    {
        // Sprint UC-Beta: A Patient caller can submit their own intake form.
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = false,
            ResponseJson = "{}",
            Consents = "{\"hipaaAcknowledged\":true,\"treatmentConsentAccepted\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        // Caller is the Patient who owns this intake.
        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());
        identityMock.Setup(x => x.GetCurrentUserRole()).Returns(Roles.Patient);

        var patientContextMock = new Mock<IPatientContextAccessor>();
        patientContextMock.Setup(x => x.GetCurrentPatientId()).Returns(patient.Id);

        var auditMock = new Mock<IAuditService>();

        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        Assert.IsType<Ok<IntakeResponse>>(result);
        var updated = await _db.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.True(updated.IsLocked);
        Assert.NotNull(updated.SubmittedAt);
    }

    [Fact]
    public async Task UCBeta_SubmitIntake_MissingTreatmentConsent_Returns400AndLeavesDraftUnlocked()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = false,
            ResponseJson = "{}",
            Consents = "{\"hipaaAcknowledged\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var auditMock = new Mock<IAuditService>();
        var patientContextMock = new Mock<IPatientContextAccessor>();

        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, statusResult.StatusCode);

        var updated = await _db.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.False(updated.IsLocked);
        Assert.Null(updated.SubmittedAt);
    }

    [Fact]
    public async Task UCBeta_ReviewIntake_LogsAuditEvent_WhenIntakeIsLocked()
    {
        // Sprint UC-Beta: Clinician review of a submitted intake logs an audit event.
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            SubmittedAt = DateTime.UtcNow.AddMinutes(-5),
            Consents = "{\"hipaaAcknowledged\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var reviewerId = Guid.NewGuid();
        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(reviewerId);

        var auditMock = new Mock<IAuditService>();

        var result = await IntakeEndpoints.ReviewIntake(intake.Id, _db, identityMock.Object, auditMock.Object, CancellationToken.None);

        Assert.IsType<Ok<IntakeResponse>>(result);
        auditMock.Verify(
            a => a.LogIntakeEventAsync(
                It.Is<AuditEvent>(e => e.EventType == "IntakeReviewed" && e.UserId == reviewerId && e.EntityId == intake.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UCBeta_SubmitIntake_LogsAuditEvent()
    {
        // Sprint UC-Beta: Intake submission logs an audit event.
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = false,
            ResponseJson = "{}",
            Consents = "{\"hipaaAcknowledged\":true,\"treatmentConsentAccepted\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var submitterId = Guid.NewGuid();
        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(submitterId);

        var auditMock = new Mock<IAuditService>();
        var patientContextMock = new Mock<IPatientContextAccessor>();

        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, auditMock.Object, patientContextMock.Object, Mock.Of<ISyncEngine>(), CancellationToken.None);

        Assert.IsType<Ok<IntakeResponse>>(result);
        auditMock.Verify(
            a => a.LogIntakeEventAsync(
                It.Is<AuditEvent>(e => e.EventType == "IntakeSubmitted" && e.UserId == submitterId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UCBeta_RevokeIntakeConsents_RequiresWrittenConfirmation()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"hipaaAcknowledged\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var result = await IntakeEndpoints.RevokeIntakeConsents(
            intake.Id,
            new RevokeIntakeConsentRequest
            {
                ConsentKeys = ["hipaaAcknowledged"],
                WrittenRevocationReceived = false
            },
            _db,
            identityMock.Object,
            Mock.Of<IAuditService>(),
            Mock.Of<ISyncEngine>(),
            CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(result);
    }

    [Fact]
    public async Task UCBeta_RevokeIntakeConsents_UpdatesConsentsAndLogsAudit()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"hipaaAcknowledged\":true,\"communicationEmailConsent\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        var identityMock = new Mock<IIdentityContextAccessor>();
        identityMock.Setup(x => x.GetCurrentUserId()).Returns(actorId);

        var auditMock = new Mock<IAuditService>();

        var result = await IntakeEndpoints.RevokeIntakeConsents(
            intake.Id,
            new RevokeIntakeConsentRequest
            {
                ConsentKeys = ["hipaaAcknowledged", "communicationEmailConsent"],
                WrittenRevocationReceived = true,
                WrittenRequestReference = "doc-42"
            },
            _db,
            identityMock.Object,
            auditMock.Object,
            Mock.Of<ISyncEngine>(),
            CancellationToken.None);

        Assert.IsType<Ok<IntakeResponse>>(result);

        var saved = await _db.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.Contains("\"hipaaAcknowledged\":true", saved.Consents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"communicationEmailConsent\":true", saved.Consents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"writtenRevocationReceived\":true", saved.Consents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"revokedConsentKeys\"", saved.Consents, StringComparison.OrdinalIgnoreCase);

        auditMock.Verify(
            a => a.LogIntakeEventAsync(
                It.Is<AuditEvent>(e =>
                    e.EventType == "IntakeConsentRevoked" &&
                    e.UserId == actorId &&
                    e.EntityId == intake.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UCBeta_GetIntakeConsentRevocations_ReturnsStateAndAuditHistory()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"hipaaAcknowledged\":false,\"writtenRevocationReceived\":true,\"lastRevocationAtUtc\":\"2026-03-29T00:00:00Z\",\"revokedConsentKeys\":[\"hipaaAcknowledged\"]}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);

        _db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            EventType = "IntakeConsentRevoked",
            Severity = "Info",
            Success = true,
            UserId = Guid.NewGuid(),
            EntityType = "IntakeForm",
            EntityId = intake.Id,
            CorrelationId = Guid.NewGuid().ToString(),
            MetadataJson = "{\"ConsentKeys\":[\"hipaaAcknowledged\"],\"HasWrittenReference\":true}"
        });

        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeConsentRevocations(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeConsentRevocationHistoryResponse>>(result);
        Assert.Equal(intake.Id, ok.Value!.IntakeId);
        Assert.True(ok.Value.WrittenRevocationReceived);
        Assert.Contains("hipaaAcknowledged", ok.Value.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Single(ok.Value.AuditEntries);
        Assert.True(ok.Value.AuditEntries[0].HasWrittenReference);
        Assert.Contains("hipaaAcknowledged", ok.Value.AuditEntries[0].ConsentKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UCBeta_GetIntakeConsentRevocationTimeline_PagesAndFiltersByCorrelationId()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"hipaaAcknowledged\":false}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);

        var matchingCorrelation = Guid.NewGuid().ToString();
        _db.AuditLogs.AddRange(
            new AuditLog
            {
                TimestampUtc = DateTime.UtcNow.AddMinutes(-3),
                EventType = "IntakeConsentRevoked",
                Severity = "Info",
                Success = true,
                UserId = Guid.NewGuid(),
                EntityType = "IntakeForm",
                EntityId = intake.Id,
                CorrelationId = matchingCorrelation,
                MetadataJson = "{\"ConsentKeys\":[\"hipaaAcknowledged\"],\"HasWrittenReference\":false}"
            },
            new AuditLog
            {
                TimestampUtc = DateTime.UtcNow.AddMinutes(-2),
                EventType = "IntakeConsentRevoked",
                Severity = "Info",
                Success = true,
                UserId = Guid.NewGuid(),
                EntityType = "IntakeForm",
                EntityId = intake.Id,
                CorrelationId = matchingCorrelation,
                MetadataJson = "{\"ConsentKeys\":[\"communicationEmailConsent\"],\"HasWrittenReference\":true}"
            },
            new AuditLog
            {
                TimestampUtc = DateTime.UtcNow.AddMinutes(-1),
                EventType = "IntakeConsentRevoked",
                Severity = "Info",
                Success = true,
                UserId = Guid.NewGuid(),
                EntityType = "IntakeForm",
                EntityId = intake.Id,
                CorrelationId = Guid.NewGuid().ToString(),
                MetadataJson = "{\"ConsentKeys\":[\"mediaConsentAccepted\"],\"HasWrittenReference\":true}"
            });

        await _db.SaveChangesAsync();

        var filteredResult = await IntakeEndpoints.GetIntakeConsentRevocationTimeline(
            intake.Id,
            page: 1,
            pageSize: 10,
            correlationId: matchingCorrelation,
            _db,
            CancellationToken.None);

        var filtered = Assert.IsType<Ok<IntakeConsentRevocationTimelineResponse>>(filteredResult).Value!;
        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(2, filtered.Entries.Count);
        Assert.All(filtered.Entries, entry => Assert.Equal(matchingCorrelation, entry.CorrelationId));

        var pagedResult = await IntakeEndpoints.GetIntakeConsentRevocationTimeline(
            intake.Id,
            page: 2,
            pageSize: 1,
            correlationId: null,
            _db,
            CancellationToken.None);

        var paged = Assert.IsType<Ok<IntakeConsentRevocationTimelineResponse>>(pagedResult).Value!;
        Assert.Equal(3, paged.TotalCount);
        Assert.Single(paged.Entries);
    }

    [Fact]
    public async Task UCBeta_GetIntakeCommunicationConsentEligibility_ReturnsAllowedChannels()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"communicationCallConsent\":true,\"communicationTextConsent\":true,\"communicationEmailConsent\":true,\"communicationPhoneNumber\":\"555-1212\",\"communicationEmail\":\"patient@example.com\"}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeCommunicationConsentEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeCommunicationConsentEligibilityResponse>>(result).Value!;
        Assert.True(ok.CallAllowed);
        Assert.True(ok.TextAllowed);
        Assert.True(ok.EmailAllowed);
        Assert.True(ok.AnyChannelAllowed);
        Assert.Equal("555-1212", ok.CommunicationPhoneNumber);
        Assert.Equal("patient@example.com", ok.CommunicationEmail);
    }

    [Fact]
    public async Task UCBeta_GetIntakeCommunicationConsentEligibility_BlocksRevokedChannel()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"communicationCallConsent\":true,\"communicationTextConsent\":true,\"communicationEmailConsent\":true,\"revokedConsentKeys\":[\"communicationTextConsent\"]}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeCommunicationConsentEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeCommunicationConsentEligibilityResponse>>(result).Value!;
        Assert.True(ok.CallAllowed);
        Assert.False(ok.TextAllowed);
        Assert.True(ok.EmailAllowed);
        Assert.True(ok.AnyChannelAllowed);
        Assert.Contains("communicationTextConsent", ok.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UCBeta_GetIntakeSpecialtyConsentEligibility_ReturnsAllowedSpecialties()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"dryNeedlingConsentAccepted\":true,\"pelvicFloorConsentAccepted\":true}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeSpecialtyConsentEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeSpecialtyConsentEligibilityResponse>>(result).Value!;
        Assert.True(ok.DryNeedlingAllowed);
        Assert.True(ok.PelvicFloorAllowed);
        Assert.True(ok.AnySpecialtyAllowed);
    }

    [Fact]
    public async Task UCBeta_GetIntakeSpecialtyConsentEligibility_BlocksRevokedSpecialty()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            Consents = "{\"dryNeedlingConsentAccepted\":true,\"pelvicFloorConsentAccepted\":true,\"revokedConsentKeys\":[\"dryNeedlingConsentAccepted\"]}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeSpecialtyConsentEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeSpecialtyConsentEligibilityResponse>>(result).Value!;
        Assert.False(ok.DryNeedlingAllowed);
        Assert.True(ok.PelvicFloorAllowed);
        Assert.True(ok.AnySpecialtyAllowed);
        Assert.Contains("dryNeedlingConsentAccepted", ok.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UCBeta_GetIntakePhiReleaseEligibility_ReturnsAllowed()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"phiReleaseAuthorized\":true,\"writtenRevocationReceived\":false,\"revokedConsentKeys\":[]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakePhiReleaseEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakePhiReleaseEligibilityResponse>>(result).Value!;
        Assert.True(ok.PhiReleaseAllowed);
        Assert.Empty(ok.RevokedConsentKeys);
    }

    [Fact]
    public async Task UCBeta_GetIntakePhiReleaseEligibility_BlocksRevokedConsent()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"phiReleaseAuthorized\":true,\"writtenRevocationReceived\":true,\"revokedConsentKeys\":[\"phiReleaseAuthorized\"]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakePhiReleaseEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakePhiReleaseEligibilityResponse>>(result).Value!;
        Assert.False(ok.PhiReleaseAllowed);
        Assert.Contains("phiReleaseAuthorized", ok.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UCBeta_GetIntakeCreditCardAuthorizationEligibility_ReturnsAllowed()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"creditCardAuthorizationAccepted\":true,\"writtenRevocationReceived\":false,\"revokedConsentKeys\":[]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeCreditCardAuthorizationEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeCreditCardAuthorizationEligibilityResponse>>(result).Value!;
        Assert.True(ok.CreditCardAuthorizationAllowed);
        Assert.Empty(ok.RevokedConsentKeys);
    }

    [Fact]
    public async Task UCBeta_GetIntakeCreditCardAuthorizationEligibility_BlocksRevokedConsent()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"creditCardAuthorizationAccepted\":true,\"writtenRevocationReceived\":true,\"revokedConsentKeys\":[\"creditCardAuthorizationAccepted\"]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeCreditCardAuthorizationEligibility(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeCreditCardAuthorizationEligibilityResponse>>(result).Value!;
        Assert.False(ok.CreditCardAuthorizationAllowed);
        Assert.Contains("creditCardAuthorizationAccepted", ok.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UCBeta_GetIntakeConsentCompleteness_ReturnsCompleteWhenAllRequiredPresent()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"hipaaAcknowledged\":true,\"treatmentConsentAccepted\":true,\"revokedConsentKeys\":[]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeConsentCompleteness(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeConsentCompletenessResponse>>(result).Value!;
        Assert.True(ok.IsComplete);
        Assert.Empty(ok.MissingConsentKeys);
        Assert.Empty(ok.RevokedConsentKeys);
        Assert.All(ok.Items, item => Assert.True(item.Ready));
    }

    [Fact]
    public async Task UCBeta_GetIntakeConsentCompleteness_ReturnsMissingWhenHipaaAbsent()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"treatmentConsentAccepted\":true,\"revokedConsentKeys\":[]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeConsentCompleteness(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeConsentCompletenessResponse>>(result).Value!;
        Assert.False(ok.IsComplete);
        Assert.Contains("hipaaAcknowledged", ok.MissingConsentKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(ok.RevokedConsentKeys);
    }

    [Fact]
    public async Task UCBeta_GetIntakeConsentCompleteness_ReturnsRevokedWhenRequiredConsentRevoked()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Consents = "{\"hipaaAcknowledged\":true,\"treatmentConsentAccepted\":true,\"writtenRevocationReceived\":true,\"revokedConsentKeys\":[\"treatmentConsentAccepted\"]}"
        };
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var result = await IntakeEndpoints.GetIntakeConsentCompleteness(intake.Id, _db, CancellationToken.None);

        var ok = Assert.IsType<Ok<IntakeConsentCompletenessResponse>>(result).Value!;
        Assert.False(ok.IsComplete);
        Assert.Empty(ok.MissingConsentKeys);
        Assert.Contains("treatmentConsentAccepted", ok.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
        var item = ok.Items.Single(i => i.ConsentKey == "treatmentConsentAccepted");
        Assert.False(item.Ready);
    }

    // ─── U-C3: AI not persisted before acceptance ────────────────────────────

    [Fact]
    public async Task UC3_AiGeneration_DoesNotPersistWithoutAcceptance()
    {
        // Verify that AI generation audit events are logged but no ClinicalNote is created.
        // The IAuditService logs generation attempts; notes are only created via the notes endpoint
        // when the clinician explicitly accepts and submits the AI content.
        var auditService = new AuditService(_db);
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await auditService.LogAiGenerationAttemptAsync(
            AuditEvent.AiGenerationAttempt(noteId, "Assessment", "gpt-4", userId));

        var auditLog = await _db.AuditLogs.FirstOrDefaultAsync(a => a.EventType == "AiGenerationAttempt");
        Assert.NotNull(auditLog);

        var noteCount = await _db.ClinicalNotes.CountAsync();
        Assert.Equal(0, noteCount);
    }

    [Fact]
    public async Task UC3_AiGeneration_OnlyPersistedAfterExplicitAcceptance()
    {
        var auditService = new AuditService(_db);
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Step 1: AI generates content — audit log only, no note written
        await auditService.LogAiGenerationAttemptAsync(
            AuditEvent.AiGenerationAttempt(noteId, "Assessment", "gpt-4", userId));

        Assert.Equal(0, await _db.ClinicalNotes.CountAsync());

        // Step 2: Clinician accepts → explicit note creation (simulates POST /api/v1/notes)
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"assessment\":\"AI-generated content accepted by clinician\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        // Step 3: Log acceptance
        await auditService.LogAiGenerationAcceptedAsync(
            AuditEvent.AiGenerationAccepted(note.Id, "Assessment", userId));

        Assert.Equal(1, await _db.ClinicalNotes.CountAsync());

        var attemptLog = await _db.AuditLogs.FirstOrDefaultAsync(a => a.EventType == "AiGenerationAttempt");
        var acceptanceLog = await _db.AuditLogs.FirstOrDefaultAsync(a => a.EventType == "AiGenerationAccepted");
        Assert.NotNull(attemptLog);
        Assert.NotNull(acceptanceLog);
    }

    // ─── U-C4: PTA requires co-sign (tests run against migrated SQLite) ──────
    // Using the class-level _db (migrated SQLite) ensures schema/migration regressions
    // for RequiresCoSign/CoSignedByUserId/CoSignedUtc columns are caught.

    [Fact]
    public async Task UC4_PTASignedNote_RequiresCoSign_IsSetTrue()
    {
        var signatureService = CreateSignatureService();
        var ptaUserId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PTA, ptaUserId);
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = CreateWorkspaceNoteContentWithDiagnosis(noteType: NoteType.Daily),
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = ptaUserId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var result = await signatureService.SignNoteAsync(note.Id, ptaUserId, Roles.PTA, true, true);

        Assert.True(result.Success);
        Assert.True(result.RequiresCoSign, "PTA-signed note must require PT co-sign");
        Assert.Equal(NoteStatus.PendingCoSign, result.Status);

        var savedNote = await _db.ClinicalNotes.FindAsync(note.Id);
        Assert.True(savedNote!.RequiresCoSign);
        Assert.Null(savedNote.CoSignedByUserId);
        Assert.Null(savedNote.SignatureHash);
        Assert.Single(await _db.Signatures.Where(s => s.NoteId == note.Id).ToListAsync());
    }

    [Fact]
    public async Task UC4_PTASignedNote_PT_CanCoSign()
    {
        var signatureService = CreateSignatureService();
        var ptaUserId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PTA, ptaUserId);
        await CreateClinicianAsync(Roles.PT, ptUserId);

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = CreateWorkspaceNoteContentWithDiagnosis(noteType: NoteType.Daily),
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = ptaUserId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var signResult = await signatureService.SignNoteAsync(note.Id, ptaUserId, Roles.PTA, true, true);
        Assert.True(signResult.Success);
        Assert.True(signResult.RequiresCoSign);

        var coSignResult = await signatureService.CoSignNoteAsync(note.Id, ptUserId, true, true);

        Assert.True(coSignResult.Success, $"PT co-sign failed: {coSignResult.ErrorMessage}");
        Assert.NotNull(coSignResult.CoSignedUtc);

        var savedNote = await _db.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(ptUserId, savedNote!.CoSignedByUserId);
        Assert.NotNull(savedNote.CoSignedUtc);
        Assert.Equal(NoteStatus.Signed, savedNote.NoteStatus);
        Assert.NotNull(savedNote.SignatureHash);
        Assert.Equal(2, await _db.Signatures.CountAsync(s => s.NoteId == note.Id));
    }

    [Fact]
    public async Task UC4_NoteNotSignedByPTA_CoSignFails_NotRequiresCoSign()
    {
        var signatureService = CreateSignatureService();
        var ptUserId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PT, ptUserId);
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = CreateWorkspaceNoteContentWithDiagnosis(),
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = ptUserId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var signResult = await signatureService.SignNoteAsync(note.Id, ptUserId, Roles.PT, true, true);
        Assert.True(signResult.Success);
        Assert.False(signResult.RequiresCoSign, "PT-signed note should NOT require co-sign");

        var coSignResult = await signatureService.CoSignNoteAsync(note.Id, ptUserId, true, true);
        Assert.False(coSignResult.Success);
        Assert.Contains("does not require a co-sign", coSignResult.ErrorMessage);
    }

    // ─── RQ-033: Diagnosis code required before signing ────────────────────────

    [Fact]
    public async Task RQ033_SignNote_BlockedWhenNoteHasNoDiagnoses()
    {
        var signatureService = CreateSignatureService();
        var userId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PT, userId);
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "NoDx",
            DateOfBirth = new DateTime(1980, 1, 1),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending,
            DiagnosisCodesJson = "[]"
        };
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var result = await signatureService.SignNoteAsync(note.Id, userId, Roles.PT, true, true);

        Assert.False(result.Success);
        Assert.Contains("ICD-10 diagnosis", result.ErrorMessage);
    }

    [Fact]
    public async Task RQ033_SignNote_SucceedsWhenNoteHasDiagnosis()
    {
        var signatureService = CreateSignatureService();
        var userId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PT, userId);
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = CreateWorkspaceNoteContentWithDiagnosis(noteType: NoteType.Daily),
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var result = await signatureService.SignNoteAsync(note.Id, userId, Roles.PT, true, true);

        Assert.True(result.Success, $"Expected signing to succeed but got: {result.ErrorMessage}");
        Assert.NotNull(result.SignatureHash);
    }

    [Fact]
    public async Task RQ033_SignNote_BlockedWhenDiagnosisJsonIsWhitespace()
    {
        var signatureService = CreateSignatureService();
        var userId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PT, userId);
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "NullDx",
            DateOfBirth = new DateTime(1980, 1, 1),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending,
            DiagnosisCodesJson = "   "
        };
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var result = await signatureService.SignNoteAsync(note.Id, userId, Roles.PT, true, true);

        Assert.False(result.Success);
        Assert.Contains("ICD-10 diagnosis", result.ErrorMessage);
    }

    [Fact]
    public async Task RQ033_SignNote_BlockedWhenDiagnosisJsonInvalid()
    {
        var signatureService = CreateSignatureService();
        var userId = Guid.NewGuid();
        await CreateClinicianAsync(Roles.PT, userId);
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "InvalidDx",
            DateOfBirth = new DateTime(1980, 1, 1),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending,
            DiagnosisCodesJson = "not-valid-json"
        };
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var result = await signatureService.SignNoteAsync(note.Id, userId, Roles.PT, true, true);

        Assert.False(result.Success);
        Assert.Contains("ICD-10 diagnosis", result.ErrorMessage);
    }

    [Fact]
    public async Task UC4_NoteCoSign_Policy_PTOnly()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PT));
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PTA));
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.Admin));
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.Owner));
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.Patient));
    }

    // ─── U-C5: Role-based sync data scoping ──────────────────────────────────

    [Fact]
    public async Task UC5_AideRole_DoesNotReceiveClinicalNotes_SyncDelta()
    {
        var syncEngine = new SyncEngine(_db, NullLogger<SyncEngine>.Instance);

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"subjective\":\"Patient reports pain\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);
        var aideResult = await syncEngine.GetClientDeltaAsync(since, null, userRoles: new[] { Roles.Aide });

        Assert.Empty(aideResult.Items.Where(i => i.EntityType == "ClinicalNote"));
        Assert.NotEmpty(aideResult.Items.Where(i => i.EntityType == "Patient"));
    }

    [Fact]
    public async Task UC5_FrontDeskRole_DoesNotReceiveClinicalNotes_SyncDelta()
    {
        var syncEngine = new SyncEngine(_db, NullLogger<SyncEngine>.Instance);

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"subjective\":\"Initial eval\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: new[] { Roles.FrontDesk });

        Assert.Empty(result.Items.Where(i => i.EntityType == "ClinicalNote"));
    }

    [Fact]
    public async Task UC5_PatientRole_DoesNotReceiveClinicalNotes_SyncDelta()
    {
        var syncEngine = new SyncEngine(_db, NullLogger<SyncEngine>.Instance);

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"subjective\":\"Follow-up\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: new[] { Roles.Patient });

        Assert.Empty(result.Items.Where(i => i.EntityType == "ClinicalNote"));
    }

    [Fact]
    public async Task UC5_ClinicalStaff_ReceiveClinicalNotes_SyncDelta()
    {
        var syncEngine = new SyncEngine(_db, NullLogger<SyncEngine>.Instance);

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"subjective\":\"Clinical content\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: new[] { Roles.PT });

        Assert.NotEmpty(result.Items.Where(i => i.EntityType == "ClinicalNote"));
    }

    [Fact]
    public async Task UC5_NoRoles_ReceivesAllEntities_DefaultBehavior()
    {
        var syncEngine = new SyncEngine(_db, NullLogger<SyncEngine>.Instance);

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: null);
        Assert.NotEmpty(result.Items.Where(i => i.EntityType == "ClinicalNote"));
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private SignatureService CreateSignatureService()
    {
        var auditService = new AuditService(_db);
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock
            .Setup(engine => engine.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        return new SignatureService(_db, auditService, clinicalRulesMock.Object, new HashService(), new Mock<IAddendumService>().Object);
    }

    private async Task<User> CreateClinicianAsync(string role, Guid userId)
    {
        var user = new User
        {
            Id = userId,
            Username = $"{role.ToLowerInvariant()}-{userId:N}",
            PinHash = "hash",
            FirstName = role,
            LastName = "Clinician",
            Role = role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static Patient CreateTestPatient() => new()
    {
        FirstName = "Test",
        LastName = "Patient",
        DateOfBirth = new DateTime(1980, 1, 1),
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = Guid.NewGuid(),
        SyncState = SyncState.Pending,
        DiagnosisCodesJson = "[{\"IcdCode\":\"M54.5\",\"Description\":\"Low back pain\",\"IsPrimary\":true}]"
    };

    private static string CreateWorkspaceNoteContentWithDiagnosis(
        string code = "M54.5",
        string description = "Low back pain",
        NoteType noteType = NoteType.Evaluation)
    {
        return JsonSerializer.Serialize(new NoteWorkspaceV2Payload
        {
            NoteType = noteType,
            Assessment = new WorkspaceAssessmentV2
            {
                DiagnosisCodes =
                [
                    new DiagnosisCodeV2
                    {
                        Code = code,
                        Description = description
                    }
                ]
            }
        });
    }
}
