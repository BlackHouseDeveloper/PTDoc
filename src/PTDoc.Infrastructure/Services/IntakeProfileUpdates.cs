using System.Text.Json;
using System.Text.Json.Nodes;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>Validates explicit profile acceptance and protects chart corrections at submission.</summary>
public static class IntakeProfileUpdates
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public const string RefreshMessage = "Saved information changed. Refresh the saved information and review it again.";

    public static async Task<string> PrepareAsync(ApplicationDbContext db, IntakeForm intake, string? incomingJson, CancellationToken ct)
    {
        var json = IntakeProfileProvenance.Preserve(intake.ResponseJson, incomingJson);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(json, Options)!;
        var before = JsonSerializer.SerializeToNode(draft, Options)!.AsObject();
        if (draft.ProfileUpdate is { } update)
        {
            var profile = await IntakeProfilePrefillReader.ReadAsync(db, intake.PatientId, intake.ClinicId, ct);
            if (intake.IsLocked || profile is null || string.IsNullOrEmpty(update.ExpectedFingerprint)
                || update.ExpectedFingerprint != profile.Fingerprint
                || !Enum.IsDefined(update.Action))
                throw new InvalidOperationException(RefreshMessage);

            switch (update.Action)
            {
                case IntakeProfileAction.FillBlankFields:
                    // Filling demographics must not refresh previously accepted insurance versions.
                    var previousPrimaryId = draft.PrefilledPrimaryPolicyId;
                    profile.ApplyTo(draft, confirmedBlankFields: true);
                    if (previousPrimaryId != draft.PrefilledPrimaryPolicyId) ClearAuthorization(draft);
                    break;
                case IntakeProfileAction.UsePrimary:
                case IntakeProfileAction.OmitPrimary:
                    if (update.Action == IntakeProfileAction.UsePrimary && profile.PrimaryPolicyAmbiguous)
                        throw new InvalidOperationException("More than one primary policy is active. Ask the clinic to reconcile the policies.");
                    if (draft.PrefilledPrimaryPolicyId != profile.PrimaryPolicyId || update.Action == IntakeProfileAction.OmitPrimary)
                        ClearAuthorization(draft);
                    ClearPrimary(draft);
                    draft.OmitPrimaryPolicy = update.Action == IntakeProfileAction.OmitPrimary;
                    if (!draft.OmitPrimaryPolicy)
                    {
                        draft.InsuranceCompanyName = profile.InsuranceCompanyName;
                        draft.MemberOrPolicyNumber = profile.MemberOrPolicyNumber;
                        draft.GroupNumber = profile.GroupNumber;
                        draft.PayerType = profile.PayerType;
                        draft.AdjusterName = profile.AdjusterName;
                        draft.AdjusterPhone = profile.AdjusterPhone;
                        draft.AdjusterEmail = profile.AdjusterEmail;
                        draft.AdjusterFax = profile.AdjusterFax;
                    }
                    draft.PrefilledPrimaryPolicyId = profile.PrimaryPolicyId;
                    draft.PrefilledPrimaryPolicyModifiedUtc = profile.PrimaryPolicyModifiedUtc;
                    break;
                case IntakeProfileAction.UseSecondary:
                case IntakeProfileAction.OmitSecondary:
                    if (update.Action == IntakeProfileAction.UseSecondary && profile.SecondaryPolicyAmbiguous)
                        throw new InvalidOperationException("More than one secondary policy is active. Ask the clinic to reconcile the policies.");
                    ClearSecondary(draft);
                    draft.OmitSecondaryPolicy = update.Action == IntakeProfileAction.OmitSecondary;
                    if (!draft.OmitSecondaryPolicy)
                    {
                        draft.SecondaryInsuranceCompanyName = profile.SecondaryInsuranceCompanyName;
                        draft.SecondaryMemberOrPolicyNumber = profile.SecondaryMemberOrPolicyNumber;
                        draft.SecondaryGroupNumber = profile.SecondaryGroupNumber;
                    }
                    draft.PrefilledSecondaryPolicyId = profile.SecondaryPolicyId;
                    draft.PrefilledSecondaryPolicyModifiedUtc = profile.SecondaryPolicyModifiedUtc;
                    break;
            }
            // Legacy drafts retain their demographic protection until a new intake is created.
            draft.ProfilePrefillSourceModifiedUtc ??= profile.SourceModifiedUtc;
        }
        if (draft.OmitPrimaryPolicy) { ClearPrimary(draft); ClearAuthorization(draft); }
        if (draft.OmitSecondaryPolicy) ClearSecondary(draft);
        draft.ProfileUpdate = null;
        var after = JsonSerializer.SerializeToNode(draft, Options)!.AsObject();
        var result = IntakeProfileProvenance.Parse(json);
        foreach (var field in after.Where(p => !JsonNode.DeepEquals(p.Value, before[p.Key])))
        {
            foreach (var key in result.Select(p => p.Key).Where(k => string.Equals(k, field.Key, StringComparison.OrdinalIgnoreCase)).ToArray())
                result.Remove(key);
            result[field.Key] = field.Value?.DeepClone();
        }
        foreach (var key in result.Select(p => p.Key).Where(k => string.Equals(k, "profileUpdate", StringComparison.OrdinalIgnoreCase)).ToArray())
            result.Remove(key);
        return result.ToJsonString(Options);
    }

    public static IntakeResponseDraft ForPatientWrite(IntakeResponseDraft submitted, Patient patient)
    {
        var draft = IntakeDraftPersistence.CreatePersistenceCopy(submitted);
        var source = draft.PrefilledDemographics;
        // Without an original field snapshot, a legacy draft cannot establish that the chart is unchanged.
        if (source is null || source.FullName != $"{patient.FirstName} {patient.LastName}".Trim()) draft.FullName = null;
        if (source is null || source.DateOfBirth != (patient.DateOfBirth == default ? (DateTime?)null : patient.DateOfBirth)) draft.DateOfBirth = null;
        if (source is null || source.EmailAddress != patient.Email) draft.EmailAddress = null;
        if (source is null || source.PhoneNumber != patient.Phone) draft.PhoneNumber = null;
        if (source is null || source.AddressLine1 != patient.AddressLine1) draft.AddressLine1 = null;
        if (source is null || source.AddressLine2 != patient.AddressLine2) draft.AddressLine2 = null;
        if (source is null || source.City != patient.City) draft.City = null;
        if (source is null || source.StateOrProvince != patient.State) draft.StateOrProvince = null;
        if (source is null || source.PostalCode != patient.ZipCode) draft.PostalCode = null;
        if (source is null || source.EmergencyContactName != patient.EmergencyContactName) draft.EmergencyContactName = null;
        if (source is null || source.EmergencyContactPhone != patient.EmergencyContactPhone) draft.EmergencyContactPhone = null;
        if (draft.OmitPrimaryPolicy) { ClearPrimary(draft); ClearAuthorization(draft); }
        if (draft.OmitSecondaryPolicy) ClearSecondary(draft);
        return draft;
    }

    private static void ClearPrimary(IntakeResponseDraft draft)
    {
        draft.InsuranceCompanyName = draft.MemberOrPolicyNumber = draft.GroupNumber = draft.PayerType = null;
        draft.AdjusterName = draft.AdjusterPhone = draft.AdjusterEmail = draft.AdjusterFax = null;
    }

    private static void ClearSecondary(IntakeResponseDraft draft)
        => draft.SecondaryInsuranceCompanyName = draft.SecondaryMemberOrPolicyNumber = draft.SecondaryGroupNumber = null;

    private static void ClearAuthorization(IntakeResponseDraft draft)
    {
        draft.AuthorizationType = draft.AuthorizationNumber = draft.AuthorizationStatus = draft.DateAuthorizationReceived = null;
        draft.AuthorizationStartDate = draft.AuthorizationEndDate = draft.NumberOfVisitsOrUnitsAuthorized = null;
        draft.TotalVisitLimit = draft.VisitsUsed = draft.VisitLimitPeriod = draft.ReAuthorizationDueDate = null;
        draft.VisitAlertThreshold = draft.AuthorizationNotes = draft.NotesComments = null;
    }
}
