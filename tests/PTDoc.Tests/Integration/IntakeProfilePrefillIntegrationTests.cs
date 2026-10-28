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

    [Fact]
    public async Task Create_DiscardsCallerLifecycleMetadataAndPreservesAnswers()
    {
        using var staff = _factory.CreateClientWithRole(Roles.PT);
        using var created = await staff.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Creation",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            Phone = "555-0107"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<PatientResponse>())!;
        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["isLocked"] = true,
            ["ISLOCKED"] = true,
            ["IsSubmitted"] = true,
            ["IntakeId"] = Guid.NewGuid(),
            ["PatientId"] = Guid.NewGuid(),
            ["SubmittedAt"] = "2000-01-01T00:00:00Z",
            ["ReviewedAtUtc"] = "2000-01-01T00:00:00Z",
            ["ReviewedByUserId"] = Guid.NewGuid(),
            ["LastModifiedUtc"] = "2000-01-01T00:00:00Z",
            ["ProfilePrefill"] = new System.Text.Json.Nodes.JsonObject { ["phoneNumber"] = "forged" },
            ["phoneNumber"] = "",
            ["customExtension"] = "keep"
        };
        using var response = await staff.PostAsJsonAsync("/api/v1/intake", new CreateIntakeRequest
        {
            PatientId = patient.Id,
            ResponseJson = payload.ToJsonString()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var intake = (await response.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.False(intake.Locked);
        Assert.Null(intake.SubmittedAt);
        Assert.Null(intake.ReviewedAtUtc);
        var raw = IntakeProfileProvenance.Parse(intake.ResponseJson);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, ReviewJson)!;
        Assert.False(draft.IsLocked);
        Assert.False(draft.IsSubmitted);
        Assert.Null(draft.IntakeId);
        Assert.Null(draft.PatientId);
        Assert.Null(draft.SubmittedAt);
        Assert.Null(draft.ReviewedAtUtc);
        Assert.Null(draft.ReviewedByUserId);
        Assert.Null(draft.LastModifiedUtc);
        Assert.Null(draft.ProfilePrefill);
        Assert.Equal("", draft.PhoneNumber);
        Assert.Equal("Creation Fixture", draft.FullName);
        Assert.Equal("555-0107", draft.PrefilledDemographics!.PhoneNumber);
        Assert.Equal("keep", raw["customExtension"]!.GetValue<string>());
        Assert.Single(raw.Where(p => string.Equals(p.Key, "isLocked", StringComparison.OrdinalIgnoreCase)));
        using var update = await staff.PutAsJsonAsync($"/api/v1/intake/{intake.Id}", new UpdateIntakeRequest
        {
            ResponseJson = intake.ResponseJson,
            Consents = ReviewConsents
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var submit = await staff.PostAsync($"/api/v1/intake/{intake.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
    }

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

    private const string ReviewConsents = """{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"finalAttestationAccepted":true}""";
    private static readonly JsonSerializerOptions ReviewJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Submit_PreservesDemographicsCorrectedAfterPrefill(bool standalone, bool withoutProvenance)
    {
        using var staff = _factory.CreateClientWithRole(Roles.PT);
        var (patient, intake) = await CreateReviewFixtureAsync(staff, withPolicies: !withoutProvenance);
        using var anonymous = _factory.CreateUnauthenticatedClient();
        var client = standalone ? anonymous : staff;
        var path = standalone ? $"/api/v1/intake/access/{intake.Id}" : $"/api/v1/intake/{intake.Id}";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (withoutProvenance)
            {
                intake.ResponseJson = IntakeProfileProvenance.Preserve(null, intake.ResponseJson);
                (await db.IntakeForms.SingleAsync(f => f.Id == intake.Id)).ResponseJson = intake.ResponseJson;
            }
            var current = await db.Patients.SingleAsync(p => p.Id == patient.Id);
            current.FirstName = "Corrected Compound";
            current.LastName = "Corrected Surname";
            current.DateOfBirth = new DateTime(1985, 2, 3);
            current.Email = "corrected@example.test";
            current.Phone = "555-0199";
            current.AddressLine1 = "New address";
            current.AddressLine2 = "New unit";
            current.City = "New city";
            current.State = "CA";
            current.ZipCode = "90001";
            current.EmergencyContactName = "New contact";
            current.EmergencyContactPhone = "555-0198";
            current.LastModifiedUtc = current.LastModifiedUtc.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        if (standalone) anonymous.DefaultRequestHeaders.Add(IntakeAccessHeaders.AccessToken,
            await CreateAccessTokenAsync(staff, anonymous, intake.Id, "corrected@example.test"));
        using var update = await client.PutAsJsonAsync(path, new UpdateIntakeRequest { ResponseJson = intake.ResponseJson, Consents = ReviewConsents });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var submit = await client.PostAsync(path + "/submit", null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var saved = (await staff.GetFromJsonAsync<PatientResponse>($"/api/v1/patients/{patient.Id}"))!;
        Assert.Equal("Corrected Compound", saved.FirstName);
        Assert.Equal("Corrected Surname", saved.LastName);
        Assert.Equal(new DateTime(1985, 2, 3), saved.DateOfBirth);
        Assert.Equal("corrected@example.test", saved.Email);
        Assert.Equal("555-0199", saved.Phone);
        Assert.Equal("New address", saved.AddressLine1);
        Assert.Equal("New unit", saved.AddressLine2);
        Assert.Equal("New city", saved.City);
        Assert.Equal("CA", saved.State);
        Assert.Equal("90001", saved.ZipCode);
        Assert.Equal("New contact", saved.EmergencyContactName);
        Assert.Equal("555-0198", saved.EmergencyContactPhone);
        var submitted = (await staff.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/patient/{patient.Id}/latest"))!;
        Assert.Equal("Mary Jane Smith", JsonSerializer.Deserialize<IntakeResponseDraft>(submitted.ResponseJson, ReviewJson)!.FullName);
    }

    [Theory]
    [InlineData(false, "omitted")]
    [InlineData(true, "omitted")]
    [InlineData(false, "null")]
    [InlineData(true, "null")]
    [InlineData(false, "forged")]
    [InlineData(true, "forged")]
    public async Task Save_PreservesServerSourceWhenClientDropsOrForgesMetadata(bool standalone, string metadata)
    {
        using var staff = _factory.CreateClientWithRole(Roles.PT);
        var (patient, intake) = await CreateReviewFixtureAsync(staff);
        using var anonymous = _factory.CreateUnauthenticatedClient();
        var client = standalone ? anonymous : staff;
        if (standalone) anonymous.DefaultRequestHeaders.Add(IntakeAccessHeaders.AccessToken,
            await CreateAccessTokenAsync(staff, anonymous, intake.Id, patient.Email!));
        var path = standalone ? $"/api/v1/intake/access/{intake.Id}" : $"/api/v1/intake/{intake.Id}";
        var source = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, ReviewJson)!;
        var incoming = System.Text.Json.Nodes.JsonNode.Parse(intake.ResponseJson)!.AsObject();
        foreach (var key in incoming.Select(p => p.Key).Where(k => k.StartsWith("prefilled", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("profilePrefill", StringComparison.OrdinalIgnoreCase)).ToArray()) incoming.Remove(key);
        if (metadata != "omitted")
        {
            incoming["PrefilledPrimaryPolicyId"] = metadata == "null" ? null : Guid.NewGuid().ToString();
            incoming["prefilledPrimaryPolicyId"] = null; // Alternate casing cannot bypass the protected fields.
            incoming["omitPrimaryPolicy"] = true;
            incoming["omitSecondaryPolicy"] = true;
        }
        incoming["customExtension"] = "keep";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var policy = await db.PatientInsurancePolicies.SingleAsync(p => p.Id == source.PrefilledPrimaryPolicyId);
            policy.MemberOrPolicyNumber = "CORRECTED";
            policy.LastModifiedUtc = policy.LastModifiedUtc.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        using var update = await client.PutAsJsonAsync(path, new UpdateIntakeRequest { ResponseJson = incoming.ToJsonString(), Consents = ReviewConsents });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var saved = JsonSerializer.Deserialize<IntakeResponseDraft>(updated.ResponseJson, ReviewJson)!;
        Assert.Equal(source.PrefilledPrimaryPolicyId, saved.PrefilledPrimaryPolicyId);
        Assert.Equal(source.PrefilledSecondaryPolicyId, saved.PrefilledSecondaryPolicyId);
        Assert.Equal(source.PrefilledPrimaryPolicyModifiedUtc, saved.PrefilledPrimaryPolicyModifiedUtc);
        Assert.NotNull(saved.PrefilledDemographics);
        Assert.False(saved.OmitPrimaryPolicy);
        Assert.False(saved.OmitSecondaryPolicy);
        using var raw = JsonDocument.Parse(updated.ResponseJson);
        Assert.Equal("keep", raw.RootElement.GetProperty("customExtension").GetString());
        using var submit = await client.PostAsync(path + "/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        await using var verification = _factory.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await verificationDb.IntakeForms.SingleAsync(f => f.Id == intake.Id)).IsLocked);
        Assert.Equal("CORRECTED", (await verificationDb.PatientInsurancePolicies.SingleAsync(p => p.Id == source.PrefilledPrimaryPolicyId)).MemberOrPolicyNumber);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    public async Task StalePolicy_CanBeExplicitlyReconciledAndResubmitted(bool standalone, bool secondary, bool omit, bool withoutProvenance)
    {
        using var staff = _factory.CreateClientWithRole(Roles.PT);
        var (patient, intake) = await CreateReviewFixtureAsync(staff);
        var original = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, ReviewJson)!;
        if (withoutProvenance)
        {
            await using var legacyScope = _factory.Services.CreateAsyncScope();
            var legacyDb = legacyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            intake.ResponseJson = IntakeProfileProvenance.Preserve(null, intake.ResponseJson);
            (await legacyDb.IntakeForms.SingleAsync(f => f.Id == intake.Id)).ResponseJson = intake.ResponseJson;
            await legacyDb.SaveChangesAsync();
        }
        using var anonymous = _factory.CreateUnauthenticatedClient();
        var client = standalone ? anonymous : staff;
        if (standalone) anonymous.DefaultRequestHeaders.Add(IntakeAccessHeaders.AccessToken,
            await CreateAccessTokenAsync(staff, anonymous, intake.Id, patient.Email!));
        var path = standalone ? $"/api/v1/intake/access/{intake.Id}" : $"/api/v1/intake/{intake.Id}";
        using var consentUpdate = await client.PutAsJsonAsync(path, new UpdateIntakeRequest { ResponseJson = intake.ResponseJson, Consents = ReviewConsents });
        Assert.Equal(HttpStatusCode.OK, consentUpdate.StatusCode);
        var beforeRead = (await consentUpdate.Content.ReadFromJsonAsync<IntakeResponse>())!.ResponseJson;
        var sourceId = secondary ? original.PrefilledSecondaryPolicyId : original.PrefilledPrimaryPolicyId;
        Guid? replacementId = null;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var policy = await db.PatientInsurancePolicies.SingleAsync(p => p.Id == sourceId);
            policy.IsArchived = true;
            db.PatientInsuranceAuthorizations.Add(new PatientInsuranceAuthorization
            {
                PatientId = patient.Id,
                ClinicId = policy.ClinicId,
                PatientInsurancePolicyId = policy.Id,
                ReferenceNumber = "KEEP-AUTH"
            });
            await db.SaveChangesAsync();
            if (!omit)
            {
                var replacement = new PatientInsurancePolicy
                {
                    PatientId = patient.Id,
                    ClinicId = policy.ClinicId,
                    CoveragePriority = policy.CoveragePriority,
                    CarrierDisplayName = "Replacement",
                    MemberOrPolicyNumber = "CURRENT"
                };
                db.PatientInsurancePolicies.Add(replacement);
                replacementId = replacement.Id;
                await db.SaveChangesAsync();
            }
        }
        using var rejected = await client.PostAsync(path + "/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var refreshPath = standalone
            ? $"/api/v1/intake/access/patient/{patient.Id}/latest"
            : $"/api/v1/intake/patient/{patient.Id}/latest";
        using var refresh = await client.GetAsync(refreshPath);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = (await refresh.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(intake.Id, refreshed.Id);
        Assert.Equal(patient.Id, refreshed.PatientId);
        Assert.NotNull(refreshed.ProfilePrefill);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(refreshed.ResponseJson, ReviewJson)!;
        if (withoutProvenance)
        {
            Assert.Null(draft.PrefilledDemographics);
            Assert.Null(draft.ProfilePrefillSourceModifiedUtc);
            Assert.Equal(beforeRead, refreshed.ResponseJson);
            // Both legacy sources are unknown; accepting one must not endorse the other.
            draft.ProfileUpdate = new IntakeProfileUpdate
            {
                ExpectedFingerprint = refreshed.ProfilePrefill!.Fingerprint,
                Action = secondary ? IntakeProfileAction.UsePrimary : IntakeProfileAction.UseSecondary
            };
            using var otherPolicy = await client.PutAsJsonAsync(path, new UpdateIntakeRequest
            {
                ResponseJson = IntakeDraftPersistence.SerializePersistenceJson(draft),
                Consents = ReviewConsents
            });
            Assert.Equal(HttpStatusCode.OK, otherPolicy.StatusCode);
            var partiallyReconciled = (await otherPolicy.Content.ReadFromJsonAsync<IntakeResponse>())!;
            draft = JsonSerializer.Deserialize<IntakeResponseDraft>(partiallyReconciled.ResponseJson, ReviewJson)!;
            // For an archived target with no replacement, there is no active row to overwrite.
            if (!omit)
            {
                using var stillRejected = await client.PostAsync(path + "/submit", null);
                Assert.Equal(HttpStatusCode.Conflict, stillRejected.StatusCode);
            }
        }
        draft.MedicalHistoryNotes = "Preserve unrelated answers";
        draft.ProfileUpdate = new IntakeProfileUpdate
        {
            ExpectedFingerprint = refreshed.ProfilePrefill!.Fingerprint,
            Action = secondary ? (omit ? IntakeProfileAction.OmitSecondary : IntakeProfileAction.UseSecondary)
                : (omit ? IntakeProfileAction.OmitPrimary : IntakeProfileAction.UsePrimary)
        };
        using var reconcile = await client.PutAsJsonAsync(path, new UpdateIntakeRequest
        {
            ResponseJson = IntakeDraftPersistence.SerializePersistenceJson(draft),
            Consents = ReviewConsents
        });
        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);
        using var submitted = await client.PostAsync(path + "/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        await using var verification = _factory.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await verificationDb.IntakeForms.SingleAsync(f => f.Id == intake.Id);
        Assert.True(stored.IsLocked);
        var saved = JsonSerializer.Deserialize<IntakeResponseDraft>(stored.ResponseJson, ReviewJson)!;
        Assert.Equal("Preserve unrelated answers", saved.MedicalHistoryNotes);
        Assert.Null(saved.ProfileUpdate);
        var auth = await verificationDb.PatientInsuranceAuthorizations.SingleAsync(a => a.PatientId == patient.Id);
        Assert.Equal(sourceId, auth.PatientInsurancePolicyId);
        Assert.Equal("KEEP-AUTH", auth.ReferenceNumber);
        if (omit)
            Assert.Null(secondary ? saved.SecondaryMemberOrPolicyNumber : saved.MemberOrPolicyNumber);
        else
        {
            Assert.Equal(replacementId, secondary ? saved.PrefilledSecondaryPolicyId : saved.PrefilledPrimaryPolicyId);
            Assert.Equal("CURRENT", (await verificationDb.PatientInsurancePolicies.SingleAsync(p => p.Id == replacementId)).MemberOrPolicyNumber);
        }
    }

    [Theory]
    [InlineData("commercial", "Commercial", InsurancePayerType.Commercial)]
    [InlineData("medicare", "Medicare", InsurancePayerType.Medicare)]
    [InlineData("medicaid", "Medicaid", InsurancePayerType.Medicaid)]
    [InlineData("tricare", "TRICARE", InsurancePayerType.Tricare)]
    [InlineData("workers_comp", "Workers' Compensation", InsurancePayerType.WorkersCompensation)]
    [InlineData("motor_vehicle", "Motor Vehicle", InsurancePayerType.MotorVehicle)]
    [InlineData("liability", "Liability", InsurancePayerType.Liability)]
    [InlineData("self_pay", "Self Pay", InsurancePayerType.SelfPay)]
    public async Task PatientInfoProviderType_NormalizesOnCreateAndUpdateBeforePrefill(string alias, string label, InsurancePayerType expected)
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);
        var payer = JsonSerializer.Serialize(new { insuranceCompanyName = "UI Carrier", providerType = alias, memberIdPolicyNumber = "UI-MEMBER" });
        using var created = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Alias",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            PayerInfoJson = payer
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<PatientResponse>())!;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var normalized = await db.PatientInsurancePolicies.SingleAsync(p => p.PatientId == patient.Id);
        Assert.Equal(expected, normalized.PayerType);
        normalized.PayerType = InsurancePayerType.Other;
        await db.SaveChangesAsync();
        using var update = await client.PutAsJsonAsync($"/api/v1/patients/{patient.Id}", new UpdatePatientRequest { PayerInfoJson = payer });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(expected, (await db.PatientInsurancePolicies.AsNoTracking().SingleAsync(p => p.PatientId == patient.Id)).PayerType);
        using var intake = await client.PostAsJsonAsync($"/api/v1/intake/drafts/{patient.Id}", new EnsureIntakeDraftRequest());
        Assert.Equal(HttpStatusCode.Created, intake.StatusCode);
        var response = (await intake.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(label, JsonSerializer.Deserialize<IntakeResponseDraft>(response.ResponseJson, ReviewJson)!.PayerType);
        using var consentUpdate = await client.PutAsJsonAsync($"/api/v1/intake/{response.Id}", new UpdateIntakeRequest
        {
            ResponseJson = response.ResponseJson,
            Consents = ReviewConsents
        });
        Assert.Equal(HttpStatusCode.OK, consentUpdate.StatusCode);
        using var submitted = await client.PostAsync($"/api/v1/intake/{response.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var afterSubmission = await db.PatientInsurancePolicies.AsNoTracking().SingleAsync(p => p.PatientId == patient.Id);
        Assert.Equal(normalized.Id, afterSubmission.Id);
        Assert.Equal(expected, afterSubmission.PayerType);
        using var canonicalUpdate = await client.PutAsJsonAsync($"/api/v1/patients/{patient.Id}", new UpdatePatientRequest
        {
            PayerInfoJson = JsonSerializer.Serialize(new { insuranceCompanyName = "UI Carrier", providerType = alias, payerType = "Medicare" })
        });
        Assert.Equal(HttpStatusCode.OK, canonicalUpdate.StatusCode);
        Assert.Equal(InsurancePayerType.Medicare, (await db.PatientInsurancePolicies.AsNoTracking().SingleAsync(p => p.PatientId == patient.Id)).PayerType);
    }

    private static async Task<(PatientResponse Patient, IntakeResponse Intake)> CreateReviewFixtureAsync(HttpClient staff, bool withPolicies = true)
    {
        using var created = await staff.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Mary Jane",
            LastName = "Smith",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = $"review-{Guid.NewGuid():N}@example.test",
            Phone = "555-0107",
            AddressLine1 = "Old address",
            PayerInfoJson = withPolicies
                ? """{"insuranceCompanyName":"Primary","memberOrPolicyNumber":"P7","secondaryInsuranceCompanyName":"Secondary","secondaryMemberOrPolicyNumber":"S7"}"""
                : "{}"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<PatientResponse>())!;
        using var intake = await staff.PostAsJsonAsync($"/api/v1/intake/drafts/{patient.Id}", new EnsureIntakeDraftRequest());
        Assert.Equal(HttpStatusCode.Created, intake.StatusCode);
        return (patient, (await intake.Content.ReadFromJsonAsync<IntakeResponse>())!);
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
