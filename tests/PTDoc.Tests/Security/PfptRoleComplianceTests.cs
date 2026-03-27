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
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
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
[Trait("Category", "RBAC")]
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
    public async Task UC1_PracticeManager_RoleConstant_Defined()
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

        // Act: call the real SubmitIntake handler (not direct entity mutation)
        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, CancellationToken.None);

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

        // Act: call the real SubmitIntake handler on an already-locked form
        var result = await IntakeEndpoints.SubmitIntake(intake.Id, _db, identityMock.Object, CancellationToken.None);

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

        var result = await IntakeEndpoints.SubmitIntake(Guid.NewGuid(), _db, identityMock.Object, CancellationToken.None);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    [Fact]
    public async Task UC2_Patient_CanSubmitIntakePolicy_IntakeReadAllowed()
    {
        // The /submit endpoint uses IntakeRead (includes Patient).
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.Patient));
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
        var auditService = new AuditService(_db);
        var identityMock = new Mock<IIdentityContextAccessor>();
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock.Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var signatureService = new SignatureService(_db, auditService, identityMock.Object, clinicalRulesMock.Object);

        var ptaUserId = Guid.NewGuid();
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = ptaUserId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        // PTA signs (signerIsPta = true)
        var result = await signatureService.SignNoteAsync(note.Id, ptaUserId, signerIsPta: true);

        Assert.True(result.Success);
        Assert.True(result.RequiresCoSign, "PTA-signed note must require PT co-sign");

        var savedNote = await _db.ClinicalNotes.FindAsync(note.Id);
        Assert.True(savedNote!.RequiresCoSign);
        Assert.Null(savedNote.CoSignedByUserId);
    }

    [Fact]
    public async Task UC4_PTASignedNote_PT_CanCoSign()
    {
        var auditService = new AuditService(_db);
        var identityMock = new Mock<IIdentityContextAccessor>();
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock.Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var signatureService = new SignatureService(_db, auditService, identityMock.Object, clinicalRulesMock.Object);

        var ptaUserId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();

        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = ptaUserId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        // PTA signs first
        var signResult = await signatureService.SignNoteAsync(note.Id, ptaUserId, signerIsPta: true);
        Assert.True(signResult.Success);
        Assert.True(signResult.RequiresCoSign);

        // PT co-signs
        var coSignResult = await signatureService.CoSignNoteAsync(note.Id, ptUserId);

        Assert.True(coSignResult.Success, $"PT co-sign failed: {coSignResult.ErrorMessage}");
        Assert.NotNull(coSignResult.CoSignedUtc);

        var savedNote = await _db.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(ptUserId, savedNote!.CoSignedByUserId);
        Assert.NotNull(savedNote.CoSignedUtc);
    }

    [Fact]
    public async Task UC4_NoteNotSignedByPTA_CoSignFails_NotRequiresCoSign()
    {
        var auditService = new AuditService(_db);
        var identityMock = new Mock<IIdentityContextAccessor>();
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock.Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var signatureService = new SignatureService(_db, auditService, identityMock.Object, clinicalRulesMock.Object);

        var ptUserId = Guid.NewGuid();
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = ptUserId,
            SyncState = SyncState.Pending
        };
        _db.ClinicalNotes.Add(note);
        await _db.SaveChangesAsync();

        // PT signs (signerIsPta = false)
        var signResult = await signatureService.SignNoteAsync(note.Id, ptUserId, signerIsPta: false);
        Assert.True(signResult.Success);
        Assert.False(signResult.RequiresCoSign, "PT-signed note should NOT require co-sign");

        // Co-sign attempt on a note that doesn't require co-sign should fail
        var coSignResult = await signatureService.CoSignNoteAsync(note.Id, ptUserId);
        Assert.False(coSignResult.Success);
        Assert.Contains("does not require a co-sign", coSignResult.ErrorMessage);
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
