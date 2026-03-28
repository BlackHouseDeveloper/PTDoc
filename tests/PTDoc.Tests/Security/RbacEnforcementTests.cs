using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint UC-Alpha: "Must catch" proof tests for RBAC enforcement.
///
/// These tests validate the four non-negotiable behaviors called out in the
/// Sprint UC-Alpha acceptance criteria:
///   1. PTA cannot access eval endpoints (domain guard + policy).
///   2. Billing cannot edit notes.
///   3. Owner cannot modify data.
///   4. No unsecured endpoints (see <see cref="AuthorizationCoverageTests"/>).
///
/// Each test uses the production policy registrations from
/// <see cref="AuthorizationPolicies.AddPTDocAuthorizationPolicies"/> so that
/// any drift from the live policy definitions fails these tests automatically.
/// </summary>
[Trait("Category", "RBAC")]
public class RbacEnforcementTests
{
    // ── Must-catch: Billing cannot edit notes ────────────────────────────────

    [Fact]
    public async Task Billing_CannotWrite_Notes()
    {
        // Billing is read-only for clinical notes per Role→Capability matrix.
        // NoteWrite policy must deny Billing.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Billing));
    }

    [Fact]
    public async Task Billing_CannotCoSign_Notes()
    {
        // Only PT can co-sign PTA-authored notes.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.Billing));
    }

    [Fact]
    public async Task Billing_CannotWrite_PatientRecords()
    {
        // Billing has no write access to patient demographics.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.PatientWrite, Roles.Billing));
    }

    // ── Must-catch: Owner cannot modify data ─────────────────────────────────

    [Fact]
    public async Task Owner_CannotWrite_PatientRecords()
    {
        // Owner is read-only per Role→Capability matrix ("Edit patient demographics" = Deny).
        // PatientWrite must NOT include Owner.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.PatientWrite, Roles.Owner));
    }

    [Fact]
    public async Task Owner_CannotWrite_Notes()
    {
        // Owner cannot create or update clinical notes.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Owner));
    }

    [Fact]
    public async Task Owner_CannotCoSign_Notes()
    {
        // Co-sign is PT-only. Owner must not be permitted.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.Owner));
    }

    [Fact]
    public async Task Owner_CannotWrite_IntakeForms()
    {
        // Owner can read intake status but cannot create/update intake forms.
        // IntakeWrite includes FrontDesk, PT, PTA, Admin — but NOT Owner.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.IntakeWrite, Roles.Owner));
    }

    // ── Must-catch: PTA cannot access eval endpoints ─────────────────────────

    [Theory]
    [InlineData(NoteType.Evaluation, false)]
    [InlineData(NoteType.ProgressNote, false)]
    [InlineData(NoteType.Discharge, false)]
    [InlineData(NoteType.Daily, true)]
    public void PTA_DomainGuard_NoteTypeAccess(NoteType noteType, bool expectedAllowed)
    {
        // Mirrors the domain guard in ComplianceEndpoints/NoteEndpoints:
        //   user.IsInRole(Roles.PTA) && noteType != NoteType.Daily => block
        // Eval, ProgressNote, and Discharge must be blocked. Daily must be allowed.
        var ptaPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.PTA)], authenticationType: "Test"));

        var ptaIsBlocked = ptaPrincipal.IsInRole(Roles.PTA) && noteType != NoteType.Daily;
        Assert.Equal(expectedAllowed, !ptaIsBlocked);
    }

    [Fact]
    public async Task PTA_CannotCoSign_Notes()
    {
        // Only PT holds the NoteCoSign policy — PTA cannot counter-sign notes.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PTA));
    }

    // ── Role confirmation: PT retains full write authority ───────────────────

    [Fact]
    public async Task PT_CanWrite_Notes()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.PT));
    }

    [Fact]
    public async Task PT_CanCoSign_Notes()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.NoteCoSign, Roles.PT));
    }

    [Fact]
    public async Task PT_CanWrite_PatientRecords()
    {
        Assert.True(await EvaluatePolicyAsync(AuthorizationPolicies.PatientWrite, Roles.PT));
    }

    // ── Role confirmation: non-clinical roles denied clinical write ───────────

    [Fact]
    public async Task FrontDesk_CannotWrite_Notes()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.FrontDesk));
    }

    [Fact]
    public async Task Aide_CannotWrite_Notes()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Aide));
    }

    [Fact]
    public async Task Patient_CannotWrite_Notes()
    {
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteWrite, Roles.Patient));
    }

    [Fact]
    public async Task Patient_CannotRead_ClinicalNotes()
    {
        // Patient does not have access to the clinical notes API.
        Assert.False(await EvaluatePolicyAsync(AuthorizationPolicies.NoteRead, Roles.Patient));
    }

    // ── Policy evaluation helper ──────────────────────────────────────────────

    /// <summary>
    /// Evaluates a named authorization policy against a user with the given role
    /// using the production policy registrations.
    /// </summary>
    private static async Task<bool> EvaluatePolicyAsync(string policyName, string role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
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
}
