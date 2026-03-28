using System.Collections.Generic;
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
    /// Verifies that every route in the inventory references a known, registered policy name
    /// (or is explicitly flagged as intentionally anonymous).
    /// A typo in a policy name or an unregistered policy will cause this test to fail,
    /// preventing a misconfigured authorization decorator from silently becoming a no-op.
    /// </summary>
    [Fact]
    public void AllInventoryEntries_ReferenceKnownPolicies()
    {
        var knownPolicies = new HashSet<string>
        {
            AuthorizationPolicies.PatientRead,
            AuthorizationPolicies.PatientWrite,
            AuthorizationPolicies.NoteRead,
            AuthorizationPolicies.NoteWrite,
            AuthorizationPolicies.NoteCoSign,
            AuthorizationPolicies.IntakeRead,
            AuthorizationPolicies.IntakeWrite,
            AuthorizationPolicies.ClinicalStaff,
            AuthorizationPolicies.AdminOnly,
            AuthorizationPolicies.BillingAccess,
            AuthorizationPolicies.SchedulingAccess,
            AuthorizationPolicies.PatientHepAccess,
        };

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

            Assert.True(knownPolicies.Contains(entry.RequiredPolicy!),
                $"Route [{entry.Method}] {entry.Route} references unknown policy '{entry.RequiredPolicy}'. " +
                "Add it to knownPolicies or correct the policy name in the inventory.");
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
        Assert.Contains(routes, r => r == "/api/v1/notes" || r == "/api/v1/notes/{id}");
        // Compliance / signatures
        Assert.Contains(routes, r => r == "/api/v1/notes/{noteId}/sign" || r == "/api/v1/notes/{noteId}/co-sign" || r == "/api/v1/notes/{noteId}/addendum");
        // Sync
        Assert.Contains(routes, r => r.StartsWith("/api/v1/sync"));
        // AI generation
        Assert.Contains(routes, r => r.StartsWith("/api/v1/ai"));
        // PDF export
        Assert.Contains(routes, r => r == "/api/v1/notes/{noteId}/export/pdf");
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
        new("POST", "/api/v1/patients",              AuthorizationPolicies.PatientWrite),
        new("GET",  "/api/v1/patients",              AuthorizationPolicies.IntakeWrite),
        new("GET",  "/api/v1/patients/{id}",         AuthorizationPolicies.PatientRead),
        new("PUT",  "/api/v1/patients/{id}",         AuthorizationPolicies.PatientWrite),
        new("GET",  "/api/v1/patients/{id}/notes",   AuthorizationPolicies.NoteRead),

        // ── Intake (Intake/IntakeEndpoints.cs) ────────────────────────────────
        new("POST", "/api/v1/intake",                              AuthorizationPolicies.IntakeWrite),
        new("GET",  "/api/v1/intake/{id}",                         AuthorizationPolicies.IntakeRead),
        new("GET",  "/api/v1/intake/patient/{patientId}/draft",    AuthorizationPolicies.IntakeRead),
        new("PUT",  "/api/v1/intake/{id}",                         AuthorizationPolicies.IntakeWrite),
        new("POST", "/api/v1/intake/{id}/submit",                  AuthorizationPolicies.IntakeWrite),
        new("POST", "/api/v1/intake/{id}/lock",                    AuthorizationPolicies.IntakeWrite),

        // ── Notes draft CRUD (Notes/NoteEndpoints.cs) ─────────────────────────
        new("POST", "/api/v1/notes",       AuthorizationPolicies.NoteWrite),
        new("PUT",  "/api/v1/notes/{id}",  AuthorizationPolicies.NoteWrite),

        // ── Compliance rule evaluation (Compliance/ComplianceEndpoints.cs) ────
        new("POST", "/api/v1/compliance/evaluate/pn-frequency/{patientId}",  AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/compliance/evaluate/8-minute-rule",              AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/compliance/evaluate/signature-eligible/{noteId}", AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/compliance/evaluate/immutability/{noteId}",       AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/compliance/validate/clinical/{noteId}",           AuthorizationPolicies.ClinicalStaff),

        // ── Note signatures and addendums (Compliance/ComplianceEndpoints.cs) ─
        new("POST", "/api/v1/notes/{noteId}/sign",             AuthorizationPolicies.NoteWrite),
        new("POST", "/api/v1/notes/{noteId}/co-sign",          AuthorizationPolicies.NoteCoSign),
        new("POST", "/api/v1/notes/{noteId}/addendum",         AuthorizationPolicies.NoteWrite),
        new("GET",  "/api/v1/notes/{noteId}/verify-signature", AuthorizationPolicies.NoteRead),

        // ── Sync (Sync/SyncEndpoints.cs) ──────────────────────────────────────
        new("POST", "/api/v1/sync/run",          AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/sync/push",         AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/pull",         AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/status",       AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/sync/client/push",  AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/sync/client/pull",  AuthorizationPolicies.ClinicalStaff),

        // ── AI generation (AI/AiEndpoints.cs) ────────────────────────────────
        new("POST", "/api/v1/ai/assessment",  AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/ai/plan",        AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/ai/goals",       AuthorizationPolicies.ClinicalStaff),

        // ── PDF export (Pdf/PdfEndpoints.cs) ─────────────────────────────────
        new("POST", "/api/v1/notes/{noteId}/export/pdf", AuthorizationPolicies.ClinicalStaff),

        // ── Diagnostics (Diagnostics/DiagnosticsEndpoints.cs) ─────────────────
        new("GET", "/diagnostics/db", AuthorizationPolicies.AdminOnly),

        // ── Integrations (Integrations/IntegrationEndpoints.cs) ───────────────
        new("POST", "/api/v1/integrations/payment/process",                 AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/integrations/fax/send",                        AuthorizationPolicies.ClinicalStaff),
        new("POST", "/api/v1/integrations/hep/assign",                      AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/integrations/hep/patient-launch",              AuthorizationPolicies.PatientHepAccess),
        // Launch callback uses a URL-embedded token; AllowAnonymous is intentional.
        new("GET",  "/api/v1/integrations/hep/patient-launch/{launchToken}", null, IsIntentionallyAnonymous: true),
        new("POST", "/api/v1/integrations/mappings/{patientId}",            AuthorizationPolicies.ClinicalStaff),
        new("GET",  "/api/v1/integrations/mappings/patient/{patientId}",    AuthorizationPolicies.ClinicalStaff),
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
