using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Notes;

/// <summary>
/// Sprint UC-Gamma: SOAP Structure Enforcement and AI Guardrail tests.
///
/// Validates all acceptance criteria from the UC-Gamma sprint:
///   1. PTA cannot create eval notes.
///   2. Patient cannot access SOAP write endpoints (policy-level).
///   3. AI output does not persist until clinician acceptance (test-backed).
///   4. Non-clinical roles cannot edit SOAP.
///   5. Carry-forward is deterministic and does not override signed content.
///   6. SOAP_SUBJECTIVE rule fires as blocking violation for Eval/PN when subjective is empty.
/// </summary>
[Trait("Category", "UcGamma")]
public class SoapGammaEnforcementTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RulesEngine _rulesEngine;
    private readonly AuditService _auditService;
    private readonly ClinicalRulesEngine _clinicalRulesEngine;
    private readonly CarryForwardService _carryForwardService;

    public SoapGammaEnforcementTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"UcGammaDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _auditService = new AuditService(_context);
        _rulesEngine = new RulesEngine(_context, _auditService);
        _clinicalRulesEngine = new ClinicalRulesEngine(_context);
        _carryForwardService = new CarryForwardService(_context);
    }

    // ── 1. PTA domain guard: cannot create non-Daily notes ───────────────────

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public void PTA_IsBlocked_FromNonDailyNoteTypes(NoteType blockedType)
    {
        // The domain guard in NoteEndpoints blocks PTA from creating Eval/PN/Discharge.
        // PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType is internal and callable from tests.
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, blockedType);

        Assert.True(isBlocked, $"PTA should be blocked from creating {blockedType} notes.");
    }

    [Fact]
    public void PTA_IsAllowed_DailyNoteType()
    {
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, NoteType.Daily);

        Assert.False(isBlocked, "PTA should be allowed to create Daily notes.");
    }

    [Fact]
    public void PT_IsNeverBlocked_FromAnyNoteType()
    {
        var ptPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PT)], authenticationType: "Test"));

        foreach (var noteType in Enum.GetValues<NoteType>())
        {
            var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptPrincipal, noteType);
            Assert.False(isBlocked, $"PT should not be blocked from {noteType} notes.");
        }
    }

    // ── 2. Patient cannot access SOAP write endpoints ────────────────────────

    [Fact]
    public async Task Patient_CannotWrite_Notes()
    {
        // NoteWrite policy must deny the Patient role.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Patient));
    }

    [Fact]
    public async Task Patient_CannotRead_ClinicalNotes()
    {
        // Patients have no access to the clinical notes API at all.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Patient));
    }

    // ── 3. AI output does not persist until explicit clinician acceptance ─────

    [Fact]
    public void MergeAiContentIntoSection_WritesContentToSpecifiedSection()
    {
        // Calling MergeAiContentIntoSection simulates what AcceptAiSuggestion does.
        // The content is NOT persisted until this merge is followed by SaveChanges.
        const string initial = "{}";
        const string aiText = "Patient presents with right shoulder pain rated 7/10.";

        var result = PTDoc.Api.Notes.NoteEndpoints.MergeAiContentIntoSection(
            initial, "subjective", aiText);

        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("subjective", out var subjProp));
        Assert.Equal(aiText, subjProp.GetString());
    }

    [Fact]
    public void MergeAiContentIntoSection_NormalizesKeyToLowerCase()
    {
        const string initial = "{}";
        var result = PTDoc.Api.Notes.NoteEndpoints.MergeAiContentIntoSection(
            initial, "Assessment", "AI assessment narrative.");

        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("assessment", out _),
            "Section key must be stored lower-case regardless of input casing.");
        Assert.False(doc.RootElement.TryGetProperty("Assessment", out _),
            "Original casing must NOT appear as a separate key.");
    }

    [Fact]
    public void MergeAiContentIntoSection_PreservesExistingSections()
    {
        const string initial = """{"subjective":"existing value"}""";
        var result = PTDoc.Api.Notes.NoteEndpoints.MergeAiContentIntoSection(
            initial, "assessment", "AI assessment text.");

        var doc = JsonDocument.Parse(result);

        // Existing section is preserved
        Assert.True(doc.RootElement.TryGetProperty("subjective", out var subjProp));
        Assert.Equal("existing value", subjProp.GetString());

        // New section is added
        Assert.True(doc.RootElement.TryGetProperty("assessment", out var assessProp));
        Assert.Equal("AI assessment text.", assessProp.GetString());
    }

    [Fact]
    public void MergeAiContentIntoSection_ReplacesExistingSection()
    {
        // When AI content for the same section is accepted again, it replaces the prior value.
        const string initial = """{"assessment":"old AI text"}""";
        var result = PTDoc.Api.Notes.NoteEndpoints.MergeAiContentIntoSection(
            initial, "assessment", "revised AI text");

        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("assessment", out var assessProp));
        Assert.Equal("revised AI text", assessProp.GetString());
    }

    [Fact]
    public void MergeAiContentIntoSection_NormalizesExistingMixedCaseKeys()
    {
        // If ContentJson was saved with PascalCase keys (e.g. from an older serializer),
        // the merge must normalize them to lower-case and avoid creating duplicate keys.
        const string initial = """{"Assessment":"existing AI text","Subjective":"chief complaint"}""";
        var result = PTDoc.Api.Notes.NoteEndpoints.MergeAiContentIntoSection(
            initial, "plan", "new plan text");

        var doc = JsonDocument.Parse(result);

        // Original PascalCase keys must be gone
        Assert.False(doc.RootElement.TryGetProperty("Assessment", out _),
            "PascalCase key 'Assessment' must be normalized away.");
        Assert.False(doc.RootElement.TryGetProperty("Subjective", out _),
            "PascalCase key 'Subjective' must be normalized away.");

        // Normalized lower-case keys must be present
        Assert.True(doc.RootElement.TryGetProperty("assessment", out var a));
        Assert.Equal("existing AI text", a.GetString());
        Assert.True(doc.RootElement.TryGetProperty("subjective", out var s));
        Assert.Equal("chief complaint", s.GetString());
        Assert.True(doc.RootElement.TryGetProperty("plan", out var p));
        Assert.Equal("new plan text", p.GetString());

        // Total key count must be exactly 3 (no duplicates)
        Assert.Equal(3, doc.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void MergeAiContentIntoSection_OverwritesMixedCaseDuplicate()
    {
        // If ContentJson has both "Assessment" and "assessment", the merge must keep only one
        // normalized key with the merged value.
        const string initial = """{"Assessment":"old value","assessment":"also old"}""";
        var result = PTDoc.Api.Notes.NoteEndpoints.MergeAiContentIntoSection(
            initial, "assessment", "new AI text");

        var doc = JsonDocument.Parse(result);

        // Only one assessment key must remain
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Single(keys.Where(k => string.Equals(k, "assessment", StringComparison.OrdinalIgnoreCase)));
        Assert.True(doc.RootElement.TryGetProperty("assessment", out var a));
        Assert.Equal("new AI text", a.GetString());
    }

    [Fact]
    public async Task AcceptAiSuggestion_IsBlocked_OnSignedNote()
    {
        // Arrange: a signed note — AI content must NOT be written to it.
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "signed-hash-abc123",
            SignedUtc = DateTime.UtcNow.AddMinutes(-5),
            SignedByUserId = Guid.NewGuid()
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act: validate immutability — the AcceptAiSuggestion endpoint performs this check
        // before writing any AI content.
        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);

        // Assert: signed note is immutable — AI content must be rejected.
        Assert.False(result.IsValid,
            "Immutability check must block AI acceptance on a signed note.");
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
    }

    [Fact]
    public async Task AcceptAiSuggestion_IsPermitted_OnDraftNote()
    {
        // Arrange: an unsigned (draft) note — AI content can be accepted.
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = null // draft
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act: immutability check passes → AI content write is allowed.
        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);

        // Assert: draft note allows writes.
        Assert.True(result.IsValid,
            "Immutability check must pass for a draft note (AI acceptance allowed).");
    }

    // ── 4. Non-clinical roles cannot edit SOAP ────────────────────────────────

    [Theory]
    [InlineData(Roles.FrontDesk)]
    [InlineData(Roles.Aide)]
    [InlineData(Roles.Billing)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Patient)]
    [InlineData(Roles.PracticeManager)]
    public async Task NonClinical_CannotWrite_Notes(string role)
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, role),
            $"Role '{role}' must be denied NoteWrite access.");
    }

    [Theory]
    [InlineData(Roles.PT)]
    [InlineData(Roles.PTA)]
    public async Task Clinical_CanWrite_Notes(string role)
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, role),
            $"Role '{role}' must have NoteWrite access.");
    }

    // ── 5. Carry-forward: deterministic, does not override signed content ─────

    [Fact]
    public async Task CarryForward_ReturnsNull_WhenNoSignedNoteExists()
    {
        // Arrange: only an unsigned (draft) note exists — not eligible for carry-forward.
        var patientId = Guid.NewGuid();
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = null // unsigned — must NOT be used as carry-forward source
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _carryForwardService.GetCarryForwardDataAsync(patientId, NoteType.Daily);

        // Assert: unsigned notes are excluded from carry-forward.
        Assert.Null(result);
    }

    [Fact]
    public async Task CarryForward_ReturnsSignedNote_AsMostRecentSource()
    {
        // Arrange: a signed Daily note and a signed Eval note for the same patient.
        var patientId = Guid.NewGuid();

        var olderNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow.AddDays(-14),
            LastModifiedUtc = DateTime.UtcNow.AddDays(-14),
            SignatureHash = "hash-eval",
            ContentJson = """{"subjective":"eval note content"}"""
        };

        var recentNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow.AddDays(-2),
            LastModifiedUtc = DateTime.UtcNow.AddDays(-2),
            SignatureHash = "hash-daily",
            ContentJson = """{"subjective":"most recent daily note content"}"""
        };

        _context.ClinicalNotes.AddRange(olderNote, recentNote);
        await _context.SaveChangesAsync();

        // Act: carry-forward for a new Daily note should pull from most recent signed note.
        var result = await _carryForwardService.GetCarryForwardDataAsync(patientId, NoteType.Daily);

        // Assert: most recent signed note is returned.
        Assert.NotNull(result);
        Assert.Equal(recentNote.Id, result.SourceNoteId);
        Assert.Equal(recentNote.ContentJson, result.ContentJson);
    }

    [Fact]
    public async Task CarryForward_SourceNote_IsReadOnly_DoesNotMutateIt()
    {
        // Arrange: a signed note.
        var patientId = Guid.NewGuid();
        var originalContentJson = """{"subjective":"original signed content"}""";
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow.AddDays(-1),
            LastModifiedUtc = DateTime.UtcNow.AddDays(-1),
            SignatureHash = "hash-signed",
            ContentJson = originalContentJson
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act: fetch carry-forward data.
        var carryForwardData = await _carryForwardService.GetCarryForwardDataAsync(patientId, NoteType.Daily);

        // Assert: the source note is unchanged in the database.
        Assert.NotNull(carryForwardData);
        var dbNote = await _context.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(originalContentJson, dbNote!.ContentJson);
        Assert.Equal("hash-signed", dbNote.SignatureHash);
    }

    [Fact]
    public async Task CarryForward_EvaluationNote_ReturnsNull()
    {
        // Evaluation notes carry forward from intake, not prior notes.
        // The service returns null for Evaluation target type.
        var patientId = Guid.NewGuid();
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow.AddDays(-7),
            LastModifiedUtc = DateTime.UtcNow.AddDays(-7),
            SignatureHash = "hash-eval"
        });
        await _context.SaveChangesAsync();

        var result = await _carryForwardService.GetCarryForwardDataAsync(patientId, NoteType.Evaluation);

        Assert.Null(result);
    }

    [Fact]
    public async Task CarryForward_DischargeNote_OnlyPullsFromEvalOrPn()
    {
        // Discharge carry-forward must not pull from a Daily note; only Eval or PN.
        var patientId = Guid.NewGuid();

        var dailyNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow.AddDays(-1),
            LastModifiedUtc = DateTime.UtcNow.AddDays(-1),
            SignatureHash = "hash-daily",
            ContentJson = """{"subjective":"daily note"}"""
        };
        var evalNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow.AddDays(-30),
            LastModifiedUtc = DateTime.UtcNow.AddDays(-30),
            SignatureHash = "hash-eval",
            ContentJson = """{"subjective":"eval note"}"""
        };
        _context.ClinicalNotes.AddRange(dailyNote, evalNote);
        await _context.SaveChangesAsync();

        // Act: Discharge carry-forward — Daily note must not be the source.
        var result = await _carryForwardService.GetCarryForwardDataAsync(patientId, NoteType.Discharge);

        Assert.NotNull(result);
        Assert.Equal(evalNote.Id, result.SourceNoteId);
        Assert.NotEqual(dailyNote.Id, result.SourceNoteId);
    }

    // ── 6. SOAP structure: SOAP_SUBJECTIVE rule at pre-sign validation ─────────

    [Theory]
    [InlineData(NoteType.Evaluation, true)]    // blocking for Eval
    [InlineData(NoteType.ProgressNote, true)]  // blocking for PN
    [InlineData(NoteType.Daily, false)]         // advisory only for Daily
    [InlineData(NoteType.Discharge, false)]     // advisory only for Discharge
    public async Task SoapSubjective_MissingSection_FiresCorrectSeverity(
        NoteType noteType, bool expectedBlocking)
    {
        // Arrange: a note with NO subjective section in ContentJson.
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = noteType,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ContentJson = "{}"  // no subjective section
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act: run clinical validation (pre-sign rules engine).
        var results = await _clinicalRulesEngine.RunClinicalValidationAsync(note.Id);

        // Assert: SOAP_SUBJECTIVE rule must fire.
        var subjectiveViolation = results.FirstOrDefault(r => r.RuleId == "SOAP_SUBJECTIVE");
        Assert.NotNull(subjectiveViolation);
        Assert.Equal(expectedBlocking, subjectiveViolation.Blocking);
    }

    [Fact]
    public async Task SoapSubjective_PresentSection_DoesNotFireRule()
    {
        // Arrange: a note with a populated subjective section.
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ContentJson = """{"subjective":"Patient reports right shoulder pain rated 6/10."}"""
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act
        var results = await _clinicalRulesEngine.RunClinicalValidationAsync(note.Id);

        // Assert: no SOAP_SUBJECTIVE violation.
        Assert.DoesNotContain(results, r => r.RuleId == "SOAP_SUBJECTIVE");
    }

    // ── 7. UpdateNote PTA domain guard ────────────────────────────────────────

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public void PTA_IsBlocked_FromUpdating_NonDailyNoteType(NoteType blockedType)
    {
        // PtaIsBlockedFromNoteType is also used for UpdateNote.
        // A PTA should not be able to edit Evaluation/PN/Discharge notes even if NoteWrite
        // policy is satisfied, because those note types are outside PTA scope of practice.
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, blockedType);

        Assert.True(isBlocked,
            $"PTA should be blocked from updating {blockedType} notes via the UpdateNote endpoint.");
    }

    [Fact]
    public void PTA_IsAllowed_UpdateDailyNote()
    {
        // PTA must be allowed to update their own Daily notes.
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, NoteType.Daily);

        Assert.False(isBlocked, "PTA should be allowed to update Daily notes.");
    }

    // ── 8. Carry-forward endpoint NoteRead authorization ─────────────────────

    [Theory]
    [InlineData(Roles.PT)]
    [InlineData(Roles.PTA)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Billing)]
    public async Task CarryForwardEndpoint_IsAccessible_ByClinicalAndBillingRoles(string role)
    {
        // NoteRead includes PT, PTA, Admin, Owner, Billing.
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, role),
            $"Role '{role}' must have NoteRead access (carry-forward endpoint).");
    }

    [Theory]
    [InlineData(Roles.Patient)]
    [InlineData(Roles.Aide)]
    [InlineData(Roles.FrontDesk)]
    public async Task CarryForwardEndpoint_IsDenied_ByNonClinicalRoles(string role)
    {
        // FrontDesk, Aide, and Patient do not have NoteRead access.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, role),
            $"Role '{role}' must NOT have NoteRead access (carry-forward endpoint).");
    }

    // ── 9. SOAP section validation in accept-ai-suggestion ───────────────────

    [Theory]
    [InlineData("subjective")]
    [InlineData("objective")]
    [InlineData("assessment")]
    [InlineData("plan")]
    [InlineData("goals")]
    [InlineData("billing")]
    [InlineData("Subjective")]   // case-insensitive
    [InlineData("ASSESSMENT")]   // case-insensitive
    public void ValidSoapSections_ContainsAllCanonicalNames(string section)
    {
        Assert.Contains(section, PTDoc.Api.Notes.NoteEndpoints.ValidSoapSections,
            StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("note")]
    [InlineData("content")]
    [InlineData("freeText")]
    [InlineData("__proto__")]
    [InlineData("contentJson")]
    public void ValidSoapSections_RejectsInvalidNames(string invalidSection)
    {
        Assert.DoesNotContain(invalidSection, PTDoc.Api.Notes.NoteEndpoints.ValidSoapSections,
            StringComparer.OrdinalIgnoreCase);
    }

    // ── 10. AI goals signed-note enforcement (server-authoritative IsNoteSigned) ─

    [Fact]
    public async Task AiGoals_SignedNote_IsDetectedAsSignedInDb()
    {
        // When the client submits IsNoteSigned=false for a note that IS signed in the DB,
        // the server must look up the actual signing state and override the client-supplied flag.
        // This test verifies that the note lookup correctly detects the signed state.
        var signedNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "real-signature-hash",
            SignedUtc = DateTime.UtcNow.AddMinutes(-10),
            SignedByUserId = Guid.NewGuid()
        };
        _context.ClinicalNotes.Add(signedNote);
        await _context.SaveChangesAsync();

        // Simulate what AiEndpoints.GenerateGoals does: look up the note and set IsNoteSigned.
        var noteFromDb = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == signedNote.Id);

        Assert.NotNull(noteFromDb);
        var serverDerivedIsNoteSigned = noteFromDb.SignatureHash != null;

        Assert.True(serverDerivedIsNoteSigned,
            "Server-derived IsNoteSigned must be true for a signed note, regardless of what the client sent.");
    }

    [Fact]
    public async Task AiGoals_UnsignedNote_IsDetectedAsDraftInDb()
    {
        // Unsigned (draft) notes should have IsNoteSigned=false, allowing AI generation.
        var draftNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = null // unsigned
        };
        _context.ClinicalNotes.Add(draftNote);
        await _context.SaveChangesAsync();

        var noteFromDb = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == draftNote.Id);

        Assert.NotNull(noteFromDb);
        var serverDerivedIsNoteSigned = noteFromDb.SignatureHash != null;

        Assert.False(serverDerivedIsNoteSigned,
            "Server-derived IsNoteSigned must be false for a draft note (AI generation allowed).");
    }

    // ── 11. AcceptAiSuggestion PTA domain guard ───────────────────────────────

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public void PTA_IsBlocked_FromAcceptingAiSuggestion_OnNonDailyNotes(NoteType blockedType)
    {
        // The PTA guard applied in AcceptAiSuggestion uses the same PtaIsBlockedFromNoteType
        // helper. PTAs must not be able to modify Eval/PN/Discharge notes via AI acceptance.
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, blockedType);

        Assert.True(isBlocked,
            $"PTA should be blocked from accepting AI suggestions on {blockedType} notes.");
    }

    [Fact]
    public void PTA_IsAllowed_AcceptingAiSuggestion_OnDailyNote()
    {
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var isBlocked = PTDoc.Api.Notes.NoteEndpoints.PtaIsBlockedFromNoteType(ptaPrincipal, NoteType.Daily);

        Assert.False(isBlocked, "PTA should be allowed to accept AI suggestions on Daily notes.");
    }

    // ── 12. HasNonEmptyContent case-insensitivity ─────────────────────────────

    [Theory]
    [InlineData("""{"Subjective":"chief complaint value"}""", "subjective")]
    [InlineData("""{"SUBJECTIVE":"chief complaint value"}""", "subjective")]
    [InlineData("""{"ChiefComplaint":"chief complaint value"}""", "chiefComplaint")]
    [InlineData("""{"CHIEFCOMPLAINT":"chief complaint value"}""", "chiefComplaint")]
    public async Task SoapSubjective_PascalCaseFieldName_DoesNotFireRule(string contentJson, string fieldName)
    {
        // ClinicalRulesEngine.HasNonEmptyContent must match field names case-insensitively.
        // A note saved with PascalCase JSON keys should satisfy the SOAP_SUBJECTIVE rule.
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ContentJson = contentJson
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var results = await _clinicalRulesEngine.RunClinicalValidationAsync(note.Id);

        var hasSoapSubjectiveViolation = results.Any(r => r.RuleId == "SOAP_SUBJECTIVE");
        Assert.False(hasSoapSubjectiveViolation,
            $"Field '{fieldName}' (via case-insensitive lookup) must satisfy the SOAP_SUBJECTIVE rule.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<bool> EvaluatePolicyAsync(string policyName, string role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());

        var sp = services.BuildServiceProvider();
        var authService = sp.GetRequiredService<IAuthorizationService>();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test");
        var user = new ClaimsPrincipal(identity);

        var result = await authService.AuthorizeAsync(user, null, policyName);
        return result.Succeeded;
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
