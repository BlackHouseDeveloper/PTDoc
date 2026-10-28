using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class IntakeProfilePrefillIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;
    public IntakeProfilePrefillIntegrationTests(PtDocApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatedPatient_ProfileAndBothIntakeCreationRoutesRetainSavedValues(bool ensure)
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var created = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Mary Jane",
            LastName = "Smith",
            DateOfBirth = new DateTime(1990, 1, 1),
            Phone = "555-0107",
            AddressLine1 = "100 Fixture Lane",
            EmergencyContactName = "Fixture Contact",
            EmergencyContactPhone = "555-0108",
            PayerInfoJson = """{"insuranceCompanyName":"Primary Fixture","memberOrPolicyNumber":"P7","secondaryInsuranceCompanyName":"Secondary Fixture","secondaryMemberIdPolicyNumber":"S7"}"""
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<PatientResponse>())!;
        using var response = ensure
            ? await client.PostAsJsonAsync($"/api/v1/intake/drafts/{patient.Id}", new EnsureIntakeDraftRequest())
            : await client.PostAsJsonAsync("/api/v1/intake", new CreateIntakeRequest { PatientId = patient.Id, TemplateVersion = "1.0" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var latest = await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/patient/{patient.Id}/latest");
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(latest!.ResponseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("555-0107", draft.PhoneNumber);
        Assert.Equal("100 Fixture Lane", draft.AddressLine1);
        Assert.Equal("P7", draft.MemberOrPolicyNumber);
        Assert.Equal("S7", draft.SecondaryMemberOrPolicyNumber);
        Assert.NotNull(draft.PrefilledPrimaryPolicyId);
        Assert.NotNull(draft.PrefilledSecondaryPolicyId);
        var profile = await client.GetFromJsonAsync<PatientResponse>($"/api/v1/patients/{patient.Id}");
        Assert.Equal("100 Fixture Lane", profile!.AddressLine1);

        using var update = await client.PutAsJsonAsync($"/api/v1/intake/{latest.Id}", new UpdateIntakeRequest
        {
            ResponseJson = latest.ResponseJson,
            Consents = """{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"finalAttestationAccepted":true}"""
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var submitted = await client.PostAsync($"/api/v1/intake/{latest.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        profile = await client.GetFromJsonAsync<PatientResponse>($"/api/v1/patients/{patient.Id}");
        Assert.Equal("Mary Jane", profile!.FirstName);
        Assert.Equal("Smith", profile.LastName);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var policies = await db.PatientInsurancePolicies.Where(policy => policy.PatientId == patient.Id).ToListAsync();
        Assert.Equal(2, policies.Count);
        Assert.Contains(policies, policy => policy.Id == draft.PrefilledPrimaryPolicyId && policy.MemberOrPolicyNumber == "P7");
        Assert.Contains(policies, policy => policy.Id == draft.PrefilledSecondaryPolicyId && policy.MemberOrPolicyNumber == "S7");
    }

    [Theory]
    [InlineData(false, false, "replaced")]
    [InlineData(false, true, "replaced")]
    [InlineData(true, false, "replaced")]
    [InlineData(true, true, "replaced")]
    [InlineData(false, false, "changed")]
    [InlineData(false, true, "changed")]
    [InlineData(true, false, "changed")]
    [InlineData(true, true, "changed")]
    [InlineData(false, false, "reordered")]
    [InlineData(true, true, "reordered")]
    [InlineData(true, false, "unchanged")]
    public async Task Submit_ValidatesPolicySourcesBeforeAnyWrites(bool standalone, bool secondary, string change)
    {
        using var staff = _factory.CreateClientWithRole(Roles.PT);
        var email = $"prefill-{Guid.NewGuid():N}@example.test";
        using var created = await staff.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Mary Jane",
            LastName = "Smith",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = email,
            PayerInfoJson = """{"insuranceCompanyName":"Primary","memberOrPolicyNumber":"P7","secondaryInsuranceCompanyName":"Secondary","secondaryMemberOrPolicyNumber":"S7"}"""
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<PatientResponse>())!;
        using var creation = await staff.PostAsJsonAsync($"/api/v1/intake/drafts/{patient.Id}", new EnsureIntakeDraftRequest());
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var intake = (await creation.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var update = await staff.PutAsJsonAsync($"/api/v1/intake/{intake.Id}", new UpdateIntakeRequest
        {
            ResponseJson = intake.ResponseJson,
            Consents = """{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"finalAttestationAccepted":true}"""
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using var anonymous = _factory.CreateUnauthenticatedClient();
        var accessToken = standalone ? await CreateAccessTokenAsync(staff, anonymous, intake.Id, email) : null;
        Guid sourceId;
        string beforeJson;
        string beforePayerJson;
        DateTime patientVersion;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var priority = secondary ? InsuranceCoveragePriority.Secondary : InsuranceCoveragePriority.Primary;
            var source = await db.PatientInsurancePolicies.SingleAsync(policy => policy.PatientId == patient.Id && policy.CoveragePriority == priority);
            sourceId = source.Id;
            db.PatientInsuranceAuthorizations.Add(new PatientInsuranceAuthorization
            {
                PatientId = patient.Id,
                ClinicId = source.ClinicId,
                PatientInsurancePolicyId = source.Id,
                ReferenceNumber = "KEEP-AUTH"
            });
            if (change == "replaced")
            {
                source.IsArchived = true;
                db.PatientInsurancePolicies.Add(new PatientInsurancePolicy
                {
                    PatientId = patient.Id,
                    ClinicId = source.ClinicId,
                    CoveragePriority = priority,
                    CarrierDisplayName = "Replacement",
                    MemberOrPolicyNumber = "REPLACEMENT"
                });
            }
            else if (change == "changed")
            {
                source.MemberOrPolicyNumber = "CORRECTED";
                source.LastModifiedUtc = source.LastModifiedUtc.AddMinutes(1);
            }
            else if (change == "reordered")
            {
                var policies = await db.PatientInsurancePolicies.Where(policy => policy.PatientId == patient.Id).ToListAsync();
                Assert.Equal(2, policies.Count);
                var primaryPolicy = policies.Single(policy => policy.CoveragePriority == InsuranceCoveragePriority.Primary);
                var secondaryPolicy = policies.Single(policy => policy.CoveragePriority == InsuranceCoveragePriority.Secondary);
                await using var transaction = await db.Database.BeginTransactionAsync();
                // SQLite checks the active-priority unique index after each row update.
                // Stage the swap through the unused priority without exposing an intermediate state.
                primaryPolicy.CoveragePriority = InsuranceCoveragePriority.Tertiary;
                await db.SaveChangesAsync();
                secondaryPolicy.CoveragePriority = InsuranceCoveragePriority.Primary;
                await db.SaveChangesAsync();
                primaryPolicy.CoveragePriority = InsuranceCoveragePriority.Secondary;
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            await db.SaveChangesAsync();
            beforeJson = (await db.IntakeForms.SingleAsync(form => form.Id == intake.Id)).ResponseJson;
            var storedPatient = await db.Patients.SingleAsync(value => value.Id == patient.Id);
            beforePayerJson = storedPatient.PayerInfoJson;
            patientVersion = storedPatient.LastModifiedUtc;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            standalone ? $"/api/v1/intake/access/{intake.Id}/submit" : $"/api/v1/intake/{intake.Id}/submit");
        if (standalone) request.Headers.Add(IntakeAccessHeaders.AccessToken, accessToken);
        using var response = await (standalone ? anonymous : staff).SendAsync(request);
        Assert.Equal(change == "unchanged" ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedIntake = await verificationDb.IntakeForms.SingleAsync(form => form.Id == intake.Id);
        var savedPatient = await verificationDb.Patients.SingleAsync(value => value.Id == patient.Id);
        Assert.Equal("Mary Jane", savedPatient.FirstName);
        Assert.Equal("Smith", savedPatient.LastName);
        if (change != "unchanged")
        {
            Assert.False(savedIntake.IsLocked);
            Assert.Null(savedIntake.SubmittedAt);
            Assert.Equal(beforeJson, savedIntake.ResponseJson);
            Assert.Equal(beforePayerJson, savedPatient.PayerInfoJson);
            Assert.Equal(patientVersion, savedPatient.LastModifiedUtc);
            Assert.False(await verificationDb.AuditLogs.AnyAsync(log => log.EntityId == intake.Id && log.EventType == "IntakeSubmitted"));
        }
        else Assert.True(savedIntake.IsLocked);
        var savedAuthorization = await verificationDb.PatientInsuranceAuthorizations.SingleAsync(value => value.PatientId == patient.Id);
        Assert.Equal(sourceId, savedAuthorization.PatientInsurancePolicyId);
        Assert.Equal("KEEP-AUTH", savedAuthorization.ReferenceNumber);
        if (change == "changed")
            Assert.Equal("CORRECTED", (await verificationDb.PatientInsurancePolicies.SingleAsync(policy => policy.Id == sourceId)).MemberOrPolicyNumber);
        if (change == "replaced")
            Assert.Contains(await verificationDb.PatientInsurancePolicies.Where(policy => policy.PatientId == patient.Id).ToListAsync(),
                policy => policy.CarrierDisplayName == "Replacement" && policy.MemberOrPolicyNumber == "REPLACEMENT");
        if (change == "reordered")
        {
            var policies = await verificationDb.PatientInsurancePolicies.Where(policy => policy.PatientId == patient.Id).ToListAsync();
            Assert.Equal(2, policies.Count);
            Assert.Equal("S7", policies.Single(policy => policy.CoveragePriority == InsuranceCoveragePriority.Primary).MemberOrPolicyNumber);
            Assert.Equal("P7", policies.Single(policy => policy.CoveragePriority == InsuranceCoveragePriority.Secondary).MemberOrPolicyNumber);
        }
    }

    private async Task<string> CreateAccessTokenAsync(HttpClient staff, HttpClient anonymous, Guid intakeId, string email)
    {
        using var link = await staff.PostAsync($"/api/v1/intake/{intakeId}/delivery/link", null);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var bundle = (await link.Content.ReadFromJsonAsync<IntakeDeliveryBundleResponse>())!;
        var query = new Uri(bundle.InviteUrl).Query.TrimStart('?').Split('&');
        var invite = Uri.UnescapeDataString(query.Single(value => value.StartsWith("invite=", StringComparison.Ordinal))["invite=".Length..]);
        using var sent = await anonymous.PostAsJsonAsync("/api/v1/intake/access/send-otp", new SendIntakeOtpRequest
        {
            InviteToken = invite,
            Contact = email,
            Channel = OtpChannel.Email
        });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var verified = await anonymous.PostAsJsonAsync("/api/v1/intake/access/verify-otp", new VerifyIntakeOtpRequest
        {
            InviteToken = invite,
            Contact = email,
            Channel = OtpChannel.Email,
            OtpCode = _factory.LastIntakeOtpCode!
        });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return (await verified.Content.ReadFromJsonAsync<IntakeInviteResult>())!.AccessToken!;
    }
}
