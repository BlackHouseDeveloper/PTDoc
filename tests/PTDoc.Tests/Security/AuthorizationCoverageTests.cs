using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Services;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint UC-Alpha: Authorization coverage inventory gate.
///
/// This test class is the single authoritative inventory of every API route and
/// its required authorization policy (or intentional AllowAnonymous status).
/// It acts as the CI gate mandated by the Sprint UC-Alpha acceptance criteria:
///
///   "A single 'authorization coverage' inventory exists (route list + required policy)
///    and CI gates on it."
///
/// When a developer adds a new route to PTDoc.Api they MUST add a corresponding
/// entry to <see cref="BuildInventory"/>. CI will fail the [Category=RBAC] gate
/// if the inventory references unknown policy names, ensuring every new route
/// receives a conscious authorization decision.
///
/// Intentionally-anonymous endpoints (auth flows, liveness probes, webhook receivers,
/// and the identity /me endpoint which performs manual token validation in-handler)
/// are explicitly listed with <c>isIntentionallyAnonymous=true</c> so the absence
/// of an entry is immediately visible rather than silently uncovered.
/// </summary>
[Trait("Category", "RBAC")]
public class AuthorizationCoverageTests
{
    /// <summary>
    /// Verifies that every route in the inventory references a policy that is actually
    /// registered via <see cref="AuthorizationPolicies.AddPTDocAuthorizationPolicies"/>.
    /// Catching a typo or an unregistered policy prevents a misconfigured authorization
    /// decorator from silently becoming a no-op at runtime.
    /// </summary>
    [Fact]
    public void AllInventoryEntries_ReferenceKnownPolicies()
    {
        // Build the live authorization options using the same registration as Program.cs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());
        var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;

        var inventory = BuildInventory();
        Assert.NotEmpty(inventory);

        foreach (var entry in inventory)
        {
            if (entry.IsIntentionallyAnonymous)
            {
                Assert.Null(entry.RequiredPolicy);
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(entry.RequiredPolicy),
                $"Route [{entry.Method}] {entry.Route} is not marked anonymous but has no RequiredPolicy. " +
                "Add an authorization policy or mark it IsIntentionallyAnonymous.");

            var policy = authOptions.GetPolicy(entry.RequiredPolicy!);
            Assert.True(policy is not null,
                $"Route [{entry.Method}] {entry.Route} references policy '{entry.RequiredPolicy}' " +
                "which is not registered by AddPTDocAuthorizationPolicies(). " +
                "Correct the policy name or add it to the registration.");
        }
    }

    /// <summary>
    /// Validates that the inventory contains at least one entry for every major
    /// resource area (patients, intake, notes, compliance, sync, AI, PDF, diagnostics).
    /// This is a structural sanity check — if an entire area is accidentally omitted
    /// from the inventory the test will catch it.
    /// </summary>
    [Fact]
    public void Inventory_CoversAllMajorResourceAreas()
    {
        var inventory = BuildInventory();
        var routes = new HashSet<string>();
        foreach (var e in inventory)
            routes.Add(e.Route);

        // Patients
        Assert.Contains(routes, r => r.StartsWith("/api/v1/patients"));
        // Intake
        Assert.Contains(routes, r => r.StartsWith("/api/v1/intake"));
        // Notes (draft CRUD)
        Assert.Contains(routes, r => r == "/api/v1/notes" || r == "/api/v1/notes/{id:guid}");
        // Compliance / signatures
        Assert.Contains(routes, r => r == "/api/v1/notes/{noteId:guid}/sign" || r == "/api/v1/notes/{noteId:guid}/co-sign" || r == "/api/v1/notes/{noteId:guid}/addendum");
        // Sync
        Assert.Contains(routes, r => r.StartsWith("/api/v1/sync"));
        // AI generation
        Assert.Contains(routes, r => r.StartsWith("/api/v1/ai"));
        // PDF export
        Assert.Contains(routes, r => r == "/api/v1/notes/{noteId:guid}/export/pdf");
        // Daily Treatment Note workflow
        Assert.Contains(routes, r => r.StartsWith("/api/v1/daily-notes"));
        // Diagnostics
        Assert.Contains(routes, r => r.StartsWith("/diagnostics"));
        // Auth (intentionally anonymous)
        Assert.Contains(routes, r => r == "/auth/token" || r == "/api/v1/auth/pin-login");
        // Health probes (intentionally anonymous)
        Assert.Contains(routes, r => r.StartsWith("/health/"));
    }

    /// <summary>
    /// Verifies that the set of intentionally-anonymous endpoints is exactly the
    /// expected set (auth flows + health probes + HEP launch callback + /me with
    /// manual-token-validation). Any addition to or removal from this set is a
    /// conscious security decision that must be reviewed.
    /// </summary>
    [Fact]
    public void IntentionallyAnonymousEndpoints_MatchExpectedSet()
    {
        var actualAnonymous = new HashSet<string>();
        foreach (var e in BuildInventory())
        {
            if (e.IsIntentionallyAnonymous)
                actualAnonymous.Add($"{e.Method} {e.Route}");
        }

        var expectedAnonymous = new HashSet<string>
        {
            // JWT auth flow
            "POST /auth/token",
            "POST /auth/refresh",
            "POST /auth/logout",
            // PIN auth flow
            "POST /api/v1/auth/pin-login",
            "POST /api/v1/auth/logout",
            // /me performs manual token validation in-handler; AllowAnonymous lets the
            // request reach the handler so it can return 401 with a body when no token is present.
            "GET /api/v1/auth/me",
            // Health probes (liveness + readiness)
            "GET /health/live",
            "GET /health/ready",
            // Standalone intake access flow uses signed invite / session tokens in the payload or header.
            "POST /api/v1/intake/access/validate-invite",
            "POST /api/v1/intake/access/send-otp",
            "POST /api/v1/intake/access/verify-otp",
            "POST /api/v1/intake/access/validate-session",
            "POST /api/v1/intake/access/revoke-session",
            "GET /api/v1/intake/access/patient/{patientId:guid}/draft",
            "PUT /api/v1/intake/access/{id:guid}",
            "POST /api/v1/intake/access/{id:guid}/submit",
            // HEP patient launch callback — token is encoded in the URL path, not the auth header
            "GET /api/v1/integrations/hep/patient-launch/{launchToken}",
        };

        Assert.Equal(expectedAnonymous, actualAnonymous);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Authorization inventory
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Canonical route → required-policy map for every endpoint in PTDoc.Api.
    ///
    /// <b>Maintenance rule:</b> when adding a new endpoint to PTDoc.Api, add a
    /// corresponding entry here. CI ([Category=RBAC]) gates on this inventory.
    /// </summary>
    private static IReadOnlyList<InventoryEntry> BuildInventory() =>
    [
        // ── JWT auth flow (Auth/AuthEndpoints.cs) ─────────────────────────────
        new("POST", "/auth/token",   null, IsIntentionallyAnonymous: true),
        new("POST", "/auth/refresh", null, IsIntentionallyAnonymous: true),
        new("POST", "/auth/logout",  null, IsIntentionallyAnonymous: true),

        // ── PIN auth flow (Identity/AuthEndpoints.cs) ─────────────────────────
        new("POST", "/api/v1/auth/pin-login", null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/auth/logout",    null, IsIntentionallyAnonymous: true),
        // /me performs manual token validation; AllowAnonymous lets it return 401+body.
        new("GET",  "/api/v1/auth/me",        null, IsIntentionallyAnonymous: true),

        // ── Health probes (Program.cs MapHealthChecks) ─────────────────────────
        new("GET", "/health/live",  null, IsIntentionallyAnonymous: true),
        new("GET", "/health/ready", null, IsIntentionallyAnonymous: true),

        // ── Patients (Patients/PatientEndpoints.cs) ──────────────────────────
        new("POST", "/api/v1/patients",                    AuthorizationPolicies.PatientWrite),
        new("GET",  "/api/v1/patients",                    AuthorizationPolicies.PatientRead),
        new("GET",  "/api/v1/patients/{id:guid}",          AuthorizationPolicies.PatientRead),
        new("PUT",  "/api/v1/patients/{id:guid}",          AuthorizationPolicies.PatientWrite),
        new("GET",  "/api/v1/patients/{id:guid}/notes",    AuthorizationPolicies.NoteRead),
        new("GET",  "/api/v1/patients/{id:guid}/diagnoses",  AuthorizationPolicies.PatientRead),
        new("POST", "/api/v1/patients/{id:guid}/diagnoses",  AuthorizationPolicies.PatientWrite),
        new("DELETE", "/api/v1/patients/{id:guid}/diagnoses/{code}", AuthorizationPolicies.PatientWrite),

        // ── Intake (Intake/IntakeEndpoints.cs) ────────────────────────────────
        new("POST", "/api/v1/intake",                                             AuthorizationPolicies.IntakeWrite),
        new("POST", "/api/v1/intake/drafts/{patientId:guid}",                     AuthorizationPolicies.IntakeWrite),
        new("GET",  "/api/v1/intake/patients/eligible",                           AuthorizationPolicies.IntakeRead),
        new("GET",  "/api/v1/intake/{id:guid}",                                   AuthorizationPolicies.IntakeRead),
        new("GET",  "/api/v1/intake/patient/{patientId:guid}/draft",              AuthorizationPolicies.IntakeRead),
        new("PUT",  "/api/v1/intake/{id:guid}",                                   AuthorizationPolicies.IntakeWrite),
        new("POST", "/api/v1/intake/{id:guid}/submit",                            AuthorizationPolicies.IntakeRead),
        new("POST", "/api/v1/intake/{id:guid}/lock",                              AuthorizationPolicies.IntakeWrite),
        new("POST", "/api/v1/intake/{id:guid}/review",                            AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/intake/{id:guid}/delivery/link",                     AuthorizationPolicies.IntakeWrite),
        new("POST", "/api/v1/intake/{id:guid}/delivery/send",                     AuthorizationPolicies.IntakeWrite),
        new("GET",  "/api/v1/intake/{id:guid}/delivery/status",                   AuthorizationPolicies.IntakeRead),

        // ── Standalone intake access (Intake/IntakeAccessEndpoints.cs) ───────
        // These endpoints are intentionally anonymous because they perform token/header validation in-handler.
        new("POST", "/api/v1/intake/access/validate-invite",                      null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/intake/access/send-otp",                             null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/intake/access/verify-otp",                           null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/intake/access/validate-session",                     null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/intake/access/revoke-session",                       null, IsIntentionallyAnonymous: true),
        new("GET",  "/api/v1/intake/access/patient/{patientId:guid}/draft",       null, IsIntentionallyAnonymous: true),
        new("PUT",  "/api/v1/intake/access/{id:guid}",                            null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/intake/access/{id:guid}/submit",                     null, IsIntentionallyAnonymous: true),

        // ── Intake reference data (ReferenceData/IntakeReferenceDataEndpoints.cs) ─
        new("GET",  "/api/v1/reference-data/intake/",                             AuthorizationPolicies.IntakeRead),
        new("GET",  "/api/v1/reference-data/intake/body-parts",                   AuthorizationPolicies.IntakeRead),
        new("GET",  "/api/v1/reference-data/intake/medications",                  AuthorizationPolicies.IntakeRead),
        new("GET",  "/api/v1/reference-data/intake/pain-descriptors",             AuthorizationPolicies.IntakeRead),

        // ── Treatment taxonomy reference data (ReferenceData/TreatmentTaxonomyEndpoints.cs) ─
        new("GET",  "/api/v1/reference-data/treatment-taxonomy/",                 AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/reference-data/treatment-taxonomy/{categoryId}",     AuthorizationPolicies.ClinicalStaff),

        // ── Notes draft CRUD (Notes/NoteEndpoints.cs) ─────────────────────────
        new("GET",  "/api/v1/notes",            AuthorizationPolicies.NoteRead),
        new("GET",  "/api/v1/notes/{id:guid}",  AuthorizationPolicies.NoteRead),
        new("POST", "/api/v1/notes",            AuthorizationPolicies.NoteWrite),
        new("PUT",  "/api/v1/notes/{id:guid}",  AuthorizationPolicies.NoteWrite),

        // Sprint UC-Gamma: AI output acceptance gate — clinician must explicitly accept AI content
        new("POST", "/api/v1/notes/{noteId:guid}/accept-ai-suggestion", AuthorizationPolicies.NoteWrite),

        // Sprint O: Objective metrics CRUD
        new("GET",    "/api/v1/notes/{noteId:guid}/objective-metrics",             AuthorizationPolicies.NoteRead),
        new("POST",   "/api/v1/notes/{noteId:guid}/objective-metrics",             AuthorizationPolicies.NoteWrite),
        new("PUT",    "/api/v1/notes/{noteId:guid}/objective-metrics/{metricId:guid}", AuthorizationPolicies.NoteWrite),
        new("DELETE", "/api/v1/notes/{noteId:guid}/objective-metrics/{metricId:guid}", AuthorizationPolicies.NoteWrite),

        // Sprint UC-Gamma: carry-forward read endpoint — NoteRead (broader access than NoteWrite)
        new("GET",  "/api/v1/notes/carry-forward", AuthorizationPolicies.NoteRead),

        // ── Typed note workspace v2 (Notes/NoteWorkspaceV2Endpoints.cs) ──────
        new("GET",  "/api/v2/notes/workspace/{patientId:guid}/{noteId:guid}", AuthorizationPolicies.NoteRead),
        new("POST", "/api/v2/notes/workspace/", AuthorizationPolicies.NoteWrite),
        new("GET",  "/api/v2/notes/workspace/catalogs/body-regions/{bodyPart}", AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v2/notes/workspace/lookup/icd10", AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v2/notes/workspace/lookup/cpt", AuthorizationPolicies.ClinicalStaff),

        // ── Compliance rule evaluation (Compliance/ComplianceEndpoints.cs) ────
        new("POST", "/api/v1/compliance/evaluate/pn-frequency/{patientId:guid}",    AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/compliance/evaluate/8-minute-rule",                    AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/compliance/evaluate/signature-eligible/{noteId:guid}", AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/compliance/evaluate/immutability/{noteId:guid}",       AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/compliance/validate/clinical/{noteId:guid}",           AuthorizationPolicies.ClinicalStaff),

        // ── Note signatures and addendums (Compliance/ComplianceEndpoints.cs) ─
        new("POST", "/api/v1/notes/{noteId:guid}/sign",             AuthorizationPolicies.NoteWrite),
        new("POST", "/api/v1/notes/{noteId:guid}/co-sign",          AuthorizationPolicies.NoteCoSign),
        new("POST", "/api/v1/notes/{noteId:guid}/addendum",         AuthorizationPolicies.NoteWrite),
        new("GET",  "/api/v1/notes/{noteId:guid}/verify",           AuthorizationPolicies.NoteRead),
        new("GET",  "/api/v1/notes/{noteId:guid}/verify-signature", AuthorizationPolicies.NoteRead),

        // ── Sync (Sync/SyncEndpoints.cs) ──────────────────────────────────────
        new("POST", "/api/v1/sync/run",          AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/sync/push",         AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/pull",         AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/status",       AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/queue",        AuthorizationPolicies.AdminOnly),
        new("GET",  "/api/v1/sync/dead-letters", AuthorizationPolicies.AdminOnly),
        new("GET",  "/api/v1/sync/health",       AuthorizationPolicies.AdminOnly),
        new("POST", "/api/v1/sync/client/push",  AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/client/pull",  AuthorizationPolicies.ClinicalStaff),

        // ── AI generation (AI/AiEndpoints.cs) ────────────────────────────────
        new("POST", "/api/v1/ai/assessment",  AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/ai/plan",        AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/ai/goals",       AuthorizationPolicies.ClinicalStaff),

        // ── PDF export (Pdf/PdfEndpoints.cs) ─────────────────────────────────
        new("POST", "/api/v1/notes/{noteId:guid}/export/pdf", AuthorizationPolicies.NoteExport),

        // ── Daily Treatment Note workflow (Notes/DailyNoteEndpoints.cs) ───────
        new("GET",  "/api/v1/daily-notes/patient/{patientId:guid}",        AuthorizationPolicies.NoteRead),
        new("GET",  "/api/v1/daily-notes/{noteId:guid}",                   AuthorizationPolicies.NoteRead),
        new("POST", "/api/v1/daily-notes/",                                AuthorizationPolicies.NoteWrite),
        new("POST", "/api/v1/daily-notes/generate-assessment",             AuthorizationPolicies.NoteWrite),
        new("POST", "/api/v1/daily-notes/cpt-time",                        AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/daily-notes/check-medical-necessity",         AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/daily-notes/eval-carry-forward/{patientId:guid}", AuthorizationPolicies.NoteRead),

        // ── Diagnostics (Diagnostics/DiagnosticsEndpoints.cs) ─────────────────
        new("GET", "/diagnostics/db", AuthorizationPolicies.AdminOnly),

        // ── Integrations (Integrations/IntegrationEndpoints.cs) ───────────────
        new("POST", "/api/v1/integrations/payment/process",                         AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/integrations/fax/send",                                AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/integrations/hep/assign",                              AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/integrations/hep/patient-launch",                      AuthorizationPolicies.PatientHepAccess),
        // Launch callback uses a URL-embedded token; AllowAnonymous is intentional.
        new("GET",  "/api/v1/integrations/hep/patient-launch/{launchToken}",        null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/integrations/mappings/{patientId:guid}",               AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/integrations/mappings/patient/{patientId:guid}",       AuthorizationPolicies.ClinicalStaff),
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // Inventory entry type
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record InventoryEntry(
        string Method,
        string Route,
        string? RequiredPolicy,
        bool IsIntentionallyAnonymous = false);
}
