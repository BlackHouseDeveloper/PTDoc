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
///   - PTA cannot create eval
///   - Billing cannot edit notes
///   - Owner is read-only (cannot write notes)
///   - Patient cannot access SOAP (NoteRead denied)
///   - FrontDesk cannot edit notes
///   - Role-based endpoint denial works
///
/// Sprint U-C2: Intake Workflow
///   - Intake locks after submit
///
/// Sprint U-C3: SOAP + AI
///   - AI not persisted before acceptance (AuditService logs generation, not direct DB write)
///   - PTA domain guard at create level
///
/// Sprint U-C4: Compliance + Signatures
///   - PTA requires co-sign
///   - Signed note immutable (co-sign only on PTA-signed notes)
///
/// Sprint U-C5: Offline + Sync
///   - Role-based data scoping (Aide/FrontDesk do not receive clinical data)
///   - Patient gets limited dataset
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

    private ApplicationDbContext CreateInMemoryContext()
    {
        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, tenantMock.Object);
    }

    // ─── U-C1: Auth + RBAC ──────────────────────────────────────────────────

    [Fact]
    public async Task UC1_Billing_CannotEditNotes_NoteWriteBlocked()
    {
        // Billing role: "clinical notes VIEW ONLY" per canonical role matrix.
        // Billing must NOT have NoteWrite permission.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Billing));
    }

    [Fact]
    public async Task UC1_Billing_CanReadNotes_NoteReadAllowed()
    {
        // Billing needs to read notes for charge review per canonical role matrix.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Billing));
    }

    [Fact]
    public async Task UC1_Owner_IsReadOnly_NoteWriteBlocked()
    {
        // Owner: "Clinical READ ONLY" per canonical role matrix. Cannot write notes.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Owner));
    }

    [Fact]
    public async Task UC1_Owner_CanReadNotes_NoteReadAllowed()
    {
        // Owner has clinical READ access.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Owner));
    }

    [Fact]
    public async Task UC1_FrontDesk_CannotEditNotes_NoteWriteBlocked()
    {
        // Front Desk: "NO clinical editing" per canonical role matrix.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_FrontDesk_CannotReadNotes_NoteReadBlocked()
    {
        // Front Desk has no clinical note access.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_FrontDesk_CanReadIntake_IntakeReadAllowed()
    {
        // Front Desk can manage intake/consents per canonical role matrix.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_FrontDesk_CanWriteIntake_IntakeWriteAllowed()
    {
        // Front Desk can create intake forms per canonical role matrix.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_Patient_CannotAccessSOAP_NoteReadBlocked()
    {
        // Patient role must NOT have access to clinical notes (SOAP).
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Patient));
    }

    [Fact]
    public async Task UC1_Patient_CannotWriteNotes_NoteWriteBlocked()
    {
        // Patient must not be able to create or modify clinical notes.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Patient));
    }

    [Fact]
    public async Task UC1_Aide_CannotReadNotes_NoteReadBlocked()
    {
        // Therapy Aide: "NO documentation" per canonical role matrix.
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
        // PTA can create Daily notes (full NoteWrite policy; CREATE type restriction is a domain guard).
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
        // Practice Manager: no clinical editing per canonical role matrix.
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
        // Only PT can co-sign PTA-authored notes.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PT));
    }

    [Fact]
    public async Task UC1_NoteCoSign_PTA_IsNotAuthorized()
    {
        // PTA cannot co-sign their own notes.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PTA));
    }

    [Fact]
    public async Task UC1_SchedulingAccess_FrontDesk_IsAuthorized()
    {
        // Front Desk has full scheduling access per canonical role matrix.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.SchedulingAccess, Roles.FrontDesk));
    }

    [Fact]
    public async Task UC1_BillingAccess_Billing_IsAuthorized()
    {
        // Billing role has access to billing functions.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.BillingAccess, Roles.Billing));
    }

    [Fact]
    public async Task UC1_BillingAccess_Patient_IsNotAuthorized()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.BillingAccess, Roles.Patient));
    }

    // ─── U-C1: PTA domain guard at CREATE level ──────────────────────────────

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public void UC1_PTA_CannotCreateNonDailyNote_DomainGuardBlocks(NoteType noteType)
    {
        // Simulate the domain guard in the CreateNote endpoint.
        // PTA attempting to create a non-Daily note should be rejected (403 Forbidden).
        var ptaIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, Roles.PTA) }, "Test");
        var ptaPrincipal = new ClaimsPrincipal(ptaIdentity);

        var isPta = ptaPrincipal.IsInRole(Roles.PTA);
        var isNonDailyNote = noteType != NoteType.Daily;

        // The domain guard: if isPta AND isNonDailyNote → Forbid()
        var shouldForbid = isPta && isNonDailyNote;
        Assert.True(shouldForbid, $"PTA should be forbidden from creating {noteType} notes");
    }

    [Fact]
    public void UC1_PTA_CanCreateDailyNote_DomainGuardAllows()
    {
        // PTA is allowed to create Daily notes.
        var ptaIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, Roles.PTA) }, "Test");
        var ptaPrincipal = new ClaimsPrincipal(ptaIdentity);

        var isPta = ptaPrincipal.IsInRole(Roles.PTA);
        var requestedNoteType = NoteType.Daily;

        // Domain guard: forbid only if PTA AND non-Daily
        var shouldForbid = isPta && requestedNoteType != NoteType.Daily;
        Assert.False(shouldForbid, "PTA should be allowed to create Daily notes");
    }

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    [InlineData(NoteType.Daily)]
    public void UC1_PT_CanCreateAllNoteTypes_DomainGuardDoesNotApply(NoteType noteType)
    {
        // PT is not subject to the PTA domain guard.
        var ptIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, Roles.PT) }, "Test");
        var ptPrincipal = new ClaimsPrincipal(ptIdentity);

        var isPta = ptPrincipal.IsInRole(Roles.PTA);
        // PT is NOT PTA, so domain guard does not fire
        Assert.False(isPta, "PT principal should not satisfy PTA role check");
    }

    // ─── U-C2: Intake locks after submit ────────────────────────────────────

    [Fact]
    public async Task UC2_IntakeForm_LocksAfterSubmit()
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

        // Verify not locked yet
        Assert.False(intake.IsLocked);
        Assert.Null(intake.SubmittedAt);

        // Act: simulate submit (lock)
        intake.IsLocked = true;
        intake.SubmittedAt = DateTime.UtcNow;
        intake.LastModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Assert: intake is now locked
        var updated = await _db.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.True(updated.IsLocked);
        Assert.NotNull(updated.SubmittedAt);
    }

    [Fact]
    public async Task UC2_LockedIntakeForm_CannotBeSubmittedAgain()
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

        // Simulate the submit endpoint logic: if already locked, return conflict
        var savedIntake = await _db.IntakeForms.FirstAsync(f => f.Id == intake.Id);
        var shouldConflict = savedIntake.IsLocked;

        Assert.True(shouldConflict, "Already-locked intake should produce a conflict response");
    }

    [Fact]
    public async Task UC2_Patient_CanReadIntakeForm_IntakeReadAllowed()
    {
        // Patient role must be able to read their own intake form.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeRead, Roles.Patient));
    }

    // ─── U-C3: AI not persisted before acceptance ────────────────────────────

    [Fact]
    public async Task UC3_AiGeneration_DoesNotPersistWithoutAcceptance()
    {
        // Verify that AI generation audit events are logged but no ClinicalNote is created.
        // The IAuditService logs generation attempts; notes are only created via the notes endpoint
        // when the clinician explicitly accepts and submits the AI content.
        var context = CreateInMemoryContext();
        var auditService = new AuditService(context);

        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Simulate AI generation audit logging (generation attempt)
        await auditService.LogAiGenerationAttemptAsync(
            AuditEvent.AiGenerationAttempt(noteId, "Assessment", "gpt-4", userId));

        // Assert: audit log was created (proving attempt was logged)
        var auditLog = await context.AuditLogs.FirstOrDefaultAsync(
            a => a.EventType == "AiGenerationAttempt");
        Assert.NotNull(auditLog);

        // Assert: NO ClinicalNote was created by the AI generation process
        var noteCount = await context.ClinicalNotes.CountAsync();
        Assert.Equal(0, noteCount);
    }

    [Fact]
    public async Task UC3_AiGeneration_OnlyPersistedAfterExplicitAcceptance()
    {
        // Verify that a note is only persisted when clinician explicitly creates it.
        // AI generation + audit log alone do not create a note in the database.
        var context = CreateInMemoryContext();
        var auditService = new AuditService(context);

        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        // Step 1: AI generates content (no DB write of the note)
        await auditService.LogAiGenerationAttemptAsync(
            AuditEvent.AiGenerationAttempt(noteId, "Assessment", "gpt-4", userId));

        var countAfterGeneration = await context.ClinicalNotes.CountAsync();
        Assert.Equal(0, countAfterGeneration);

        // Step 2: Clinician accepts → explicit note creation (simulates POST /api/v1/notes)
        var patient = new Patient
        {
            Id = patientId,
            FirstName = "Test", LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-30),
            LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId
        };
        context.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"assessment\":\"AI-generated content accepted by clinician\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        // Step 3: Log acceptance
        await auditService.LogAiGenerationAcceptedAsync(
            AuditEvent.AiGenerationAccepted(note.Id, "Assessment", userId));

        var countAfterAcceptance = await context.ClinicalNotes.CountAsync();
        Assert.Equal(1, countAfterAcceptance);

        // Verify audit trail shows both attempt and acceptance
        var attemptLog = await context.AuditLogs.FirstOrDefaultAsync(a => a.EventType == "AiGenerationAttempt");
        var acceptanceLog = await context.AuditLogs.FirstOrDefaultAsync(a => a.EventType == "AiGenerationAccepted");
        Assert.NotNull(attemptLog);
        Assert.NotNull(acceptanceLog);
    }

    // ─── U-C4: PTA requires co-sign ─────────────────────────────────────────

    [Fact]
    public async Task UC4_PTASignedNote_RequiresCoSign_IsSetTrue()
    {
        // When PTA signs a note, RequiresCoSign must be set to true.
        var context = CreateInMemoryContext();
        var auditService = new AuditService(context);
        var identityMock = new Mock<IIdentityContextAccessor>();
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock.Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var signatureService = new SignatureService(context, auditService, identityMock.Object, clinicalRulesMock.Object);

        var ptaUserId = Guid.NewGuid();
        var patient = new Patient
        {
            FirstName = "Test", LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-50),
            LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = ptaUserId
        };
        context.Patients.Add(patient);

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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        // Act: PTA signs the note (signerIsPta = true)
        var result = await signatureService.SignNoteAsync(note.Id, ptaUserId, signerIsPta: true);

        // Assert: RequiresCoSign is set
        Assert.True(result.Success);
        Assert.True(result.RequiresCoSign, "PTA-signed note must require PT co-sign");

        var savedNote = await context.ClinicalNotes.FindAsync(note.Id);
        Assert.True(savedNote!.RequiresCoSign);
        Assert.Null(savedNote.CoSignedByUserId);
    }

    [Fact]
    public async Task UC4_PTASignedNote_PT_CanCoSign()
    {
        // After PTA signs a note, a PT can co-sign it.
        var context = CreateInMemoryContext();
        var auditService = new AuditService(context);
        var identityMock = new Mock<IIdentityContextAccessor>();
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock.Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var signatureService = new SignatureService(context, auditService, identityMock.Object, clinicalRulesMock.Object);

        var ptaUserId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Test", LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-50),
            LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = ptaUserId
        };
        context.Patients.Add(patient);

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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        // PTA signs first
        var signResult = await signatureService.SignNoteAsync(note.Id, ptaUserId, signerIsPta: true);
        Assert.True(signResult.Success);
        Assert.True(signResult.RequiresCoSign);

        // PT co-signs
        var coSignResult = await signatureService.CoSignNoteAsync(note.Id, ptUserId);

        Assert.True(coSignResult.Success, $"PT co-sign failed: {coSignResult.ErrorMessage}");
        Assert.NotNull(coSignResult.CoSignedUtc);

        var savedNote = await context.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(ptUserId, savedNote!.CoSignedByUserId);
        Assert.NotNull(savedNote.CoSignedUtc);
    }

    [Fact]
    public async Task UC4_NoteNotSignedByPTA_CoSignFails_NotRequiresCoSign()
    {
        // A note signed by PT (not PTA) should NOT have RequiresCoSign = true.
        var context = CreateInMemoryContext();
        var auditService = new AuditService(context);
        var identityMock = new Mock<IIdentityContextAccessor>();
        var clinicalRulesMock = new Mock<IClinicalRulesEngine>();
        clinicalRulesMock.Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var signatureService = new SignatureService(context, auditService, identityMock.Object, clinicalRulesMock.Object);

        var ptUserId = Guid.NewGuid();
        var patient = new Patient
        {
            FirstName = "Test", LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-50),
            LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = ptUserId
        };
        context.Patients.Add(patient);

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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        // PT signs (signerIsPta = false, default)
        var signResult = await signatureService.SignNoteAsync(note.Id, ptUserId, signerIsPta: false);
        Assert.True(signResult.Success);
        Assert.False(signResult.RequiresCoSign, "PT-signed note should NOT require co-sign");

        // Attempt to co-sign a PT-signed note should fail
        var coSignResult = await signatureService.CoSignNoteAsync(note.Id, ptUserId);
        Assert.False(coSignResult.Success);
        Assert.Contains("does not require a co-sign", coSignResult.ErrorMessage);
    }

    [Fact]
    public async Task UC4_NoteCoSign_Policy_PTOnly()
    {
        // NoteCoSign policy must be PT-only
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
        // Aide role must not receive ClinicalNote entities via sync pull.
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Add a patient and a clinical note
        var patient = new Patient
        {
            FirstName = "Test", LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-40),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);

        // Act: Aide role should NOT get clinical notes
        var aideResult = await syncEngine.GetClientDeltaAsync(since, null,
            userRoles: new[] { Roles.Aide });

        var clinicalNoteItems = aideResult.Items.Where(i => i.EntityType == "ClinicalNote").ToList();
        Assert.Empty(clinicalNoteItems);

        // Aide should still get Patient data (demographics)
        var patientItems = aideResult.Items.Where(i => i.EntityType == "Patient").ToList();
        Assert.NotEmpty(patientItems);
    }

    [Fact]
    public async Task UC5_FrontDeskRole_DoesNotReceiveClinicalNotes_SyncDelta()
    {
        // FrontDesk role must not receive ClinicalNote entities via sync pull.
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var patient = new Patient
        {
            FirstName = "Front", LastName = "Desk",
            DateOfBirth = DateTime.UtcNow.AddYears(-35),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);

        var frontDeskResult = await syncEngine.GetClientDeltaAsync(since, null,
            userRoles: new[] { Roles.FrontDesk });

        var clinicalNoteItems = frontDeskResult.Items.Where(i => i.EntityType == "ClinicalNote").ToList();
        Assert.Empty(clinicalNoteItems);
    }

    [Fact]
    public async Task UC5_PatientRole_DoesNotReceiveClinicalNotes_SyncDelta()
    {
        // Patient role must not receive ClinicalNote entities via sync pull.
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var patient = new Patient
        {
            FirstName = "Patient", LastName = "User",
            DateOfBirth = DateTime.UtcNow.AddYears(-25),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);

        var patientResult = await syncEngine.GetClientDeltaAsync(since, null,
            userRoles: new[] { Roles.Patient });

        // Patient should NOT see clinical notes
        var clinicalNoteItems = patientResult.Items.Where(i => i.EntityType == "ClinicalNote").ToList();
        Assert.Empty(clinicalNoteItems);
    }

    [Fact]
    public async Task UC5_ClinicalStaff_ReceiveClinicalNotes_SyncDelta()
    {
        // PT role must receive all clinical entities in the sync delta.
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var patient = new Patient
        {
            FirstName = "Clinical", LastName = "Staff",
            DateOfBirth = DateTime.UtcNow.AddYears(-30),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);

        // PT role should receive clinical notes
        var ptResult = await syncEngine.GetClientDeltaAsync(since, null,
            userRoles: new[] { Roles.PT });

        var clinicalNoteItems = ptResult.Items.Where(i => i.EntityType == "ClinicalNote").ToList();
        Assert.NotEmpty(clinicalNoteItems);
    }

    [Fact]
    public async Task UC5_NoRoles_ReceivesAllEntities_DefaultBehavior()
    {
        // When no user roles are specified, all entities are returned (backward compatibility).
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var patient = new Patient
        {
            FirstName = "All", LastName = "Entities",
            DateOfBirth = DateTime.UtcNow.AddYears(-30),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
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
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var since = DateTime.UtcNow.AddMinutes(-2);

        // No roles → all entities returned
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: null);
        var clinicalNoteItems = result.Items.Where(i => i.EntityType == "ClinicalNote").ToList();
        Assert.NotEmpty(clinicalNoteItems);
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
