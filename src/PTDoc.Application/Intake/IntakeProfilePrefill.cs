using PTDoc.Application.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PTDoc.Application.Intake;

/// <summary>Saved demographic/policy values offered separately from intake answers.</summary>
public sealed class IntakeProfilePrefill
{
    public string? Fingerprint { get; set; }
    public bool PrimaryPolicyAmbiguous { get; set; }
    public bool SecondaryPolicyAmbiguous { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public Guid? PrimaryPolicyId { get; set; }
    public Guid? SecondaryPolicyId { get; set; }
    public DateTime? PrimaryPolicyModifiedUtc { get; set; }
    public DateTime? SecondaryPolicyModifiedUtc { get; set; }
    public DateTime SourceModifiedUtc { get; set; }
    public string? FullName { get; set; }
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? InsuranceCompanyName { get; set; }
    public string? MemberOrPolicyNumber { get; set; }
    public string? GroupNumber { get; set; }
    public string? PayerType { get; set; }
    public string? SecondaryInsuranceCompanyName { get; set; }
    public string? SecondaryMemberOrPolicyNumber { get; set; }
    public string? SecondaryGroupNumber { get; set; }
    public string? AdjusterName { get; set; }
    public string? AdjusterPhone { get; set; }
    public string? AdjusterEmail { get; set; }
    public string? AdjusterFax { get; set; }

    public bool HasValuesForBlankFields(IntakeResponseDraft draft) => !draft.IsLocked && !draft.IsSubmitted &&
        (string.IsNullOrWhiteSpace(draft.FullName) && !string.IsNullOrWhiteSpace(FullName)
            || string.IsNullOrWhiteSpace(draft.EmailAddress) && !string.IsNullOrWhiteSpace(EmailAddress)
            || string.IsNullOrWhiteSpace(draft.PhoneNumber) && !string.IsNullOrWhiteSpace(PhoneNumber)
            || string.IsNullOrWhiteSpace(draft.AddressLine1) && !string.IsNullOrWhiteSpace(AddressLine1)
            || string.IsNullOrWhiteSpace(draft.AddressLine2) && !string.IsNullOrWhiteSpace(AddressLine2)
            || string.IsNullOrWhiteSpace(draft.City) && !string.IsNullOrWhiteSpace(City)
            || string.IsNullOrWhiteSpace(draft.StateOrProvince) && !string.IsNullOrWhiteSpace(StateOrProvince)
            || string.IsNullOrWhiteSpace(draft.PostalCode) && !string.IsNullOrWhiteSpace(PostalCode)
            || string.IsNullOrWhiteSpace(draft.EmergencyContactName) && !string.IsNullOrWhiteSpace(EmergencyContactName)
            || string.IsNullOrWhiteSpace(draft.EmergencyContactPhone) && !string.IsNullOrWhiteSpace(EmergencyContactPhone)
            || draft.DateOfBirth is null && DateOfBirth is not null
            || !draft.OmitPrimaryPolicy && !PrimaryPolicyAmbiguous && string.IsNullOrWhiteSpace(draft.InsuranceCompanyName) && string.IsNullOrWhiteSpace(draft.MemberOrPolicyNumber)
                && string.IsNullOrWhiteSpace(draft.GroupNumber) && string.IsNullOrWhiteSpace(draft.PayerType)
                && string.IsNullOrWhiteSpace(draft.AdjusterName) && string.IsNullOrWhiteSpace(draft.AdjusterPhone)
                && string.IsNullOrWhiteSpace(draft.AdjusterEmail) && string.IsNullOrWhiteSpace(draft.AdjusterFax)
                && (!string.IsNullOrWhiteSpace(InsuranceCompanyName) || !string.IsNullOrWhiteSpace(MemberOrPolicyNumber)
                    || !string.IsNullOrWhiteSpace(GroupNumber) || !string.IsNullOrWhiteSpace(PayerType)
                    || !string.IsNullOrWhiteSpace(AdjusterName) || !string.IsNullOrWhiteSpace(AdjusterPhone)
                    || !string.IsNullOrWhiteSpace(AdjusterEmail) || !string.IsNullOrWhiteSpace(AdjusterFax))
            || !draft.OmitSecondaryPolicy && !SecondaryPolicyAmbiguous && string.IsNullOrWhiteSpace(draft.SecondaryInsuranceCompanyName)
                && string.IsNullOrWhiteSpace(draft.SecondaryMemberOrPolicyNumber) && string.IsNullOrWhiteSpace(draft.SecondaryGroupNumber)
                && (!string.IsNullOrWhiteSpace(SecondaryInsuranceCompanyName) || !string.IsNullOrWhiteSpace(SecondaryMemberOrPolicyNumber)
                    || !string.IsNullOrWhiteSpace(SecondaryGroupNumber)));

    public string ApplyToNewResponseJson(string? responseJson)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = string.IsNullOrWhiteSpace(responseJson) ? new JsonObject() : JsonNode.Parse(responseJson) as JsonObject
            ?? throw new JsonException("Intake response must be a JSON object.");
        // Entity identity, lifecycle state and transient offers are supplied by the server.
        string[] serverFields =
        [
            "intakeId", "patientId", "isLocked", "isSubmitted", "submittedAt",
            "reviewedAtUtc", "reviewedByUserId", "lastModifiedUtc", "profilePrefill"
        ];
        foreach (var key in original.Select(item => item.Key).Where(key => serverFields.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray())
            original.Remove(key);
        var draft = original.Deserialize<IntakeResponseDraft>(options) ?? new();
        draft.IsLocked = false;
        draft.IsSubmitted = false;
        InitializeSource(draft);
        ApplyTo(draft);
        var populated = JsonSerializer.SerializeToNode(draft, options)!.AsObject();
        string[] fields =
        [
            "isLocked",
            "isSubmitted",
            "prefilledDemographics",
            "omitPrimaryPolicy",
            "omitSecondaryPolicy",
            "profileUpdate",
            "fullName",
            "dateOfBirth",
            "emailAddress",
            "phoneNumber",
            "addressLine1",
            "addressLine2",
            "city",
            "stateOrProvince",
            "postalCode",
            "emergencyContactName",
            "emergencyContactPhone",
            "insuranceCompanyName",
            "memberOrPolicyNumber",
            "groupNumber",
            "payerType",
            "secondaryInsuranceCompanyName",
            "secondaryMemberOrPolicyNumber",
            "secondaryGroupNumber",
            "adjusterName",
            "adjusterPhone",
            "adjusterEmail",
            "adjusterFax",
            "prefilledPrimaryPolicyId",
            "prefilledSecondaryPolicyId",
            "prefilledPrimaryPolicyModifiedUtc",
            "prefilledSecondaryPolicyModifiedUtc",
            "profilePrefillSourceModifiedUtc",
        ];
        foreach (var field in fields)
        {
            var existingKey = original.Select(item => item.Key)
                .FirstOrDefault(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
            foreach (var key in original.Select(item => item.Key).Where(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase)).ToArray())
                original.Remove(key);
            original[existingKey ?? field] = populated[field]?.DeepClone();
        }
        return original.ToJsonString(options);
    }

    // Called only when the server creates an intake. Record absence as well as presence.
    public void InitializeSource(IntakeResponseDraft draft)
    {
        draft.PrefilledDemographics = JsonSerializer.Deserialize<IntakeProfilePrefill>(JsonSerializer.Serialize(this));
        draft.PrefilledPrimaryPolicyId = PrimaryPolicyId;
        draft.PrefilledSecondaryPolicyId = SecondaryPolicyId;
        draft.PrefilledPrimaryPolicyModifiedUtc = PrimaryPolicyModifiedUtc;
        draft.PrefilledSecondaryPolicyModifiedUtc = SecondaryPolicyModifiedUtc;
        draft.ProfilePrefillSourceModifiedUtc = SourceModifiedUtc;
        draft.OmitPrimaryPolicy = false;
        draft.OmitSecondaryPolicy = false;
        draft.ProfileUpdate = null;
    }

    public static string? NormalizeProviderType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "commercial" => "Commercial",
        "medicare" => "Medicare",
        "medicaid" => "Medicaid",
        "tricare" => "TRICARE",
        "workers_comp" => "Workers' Compensation",
        "motor_vehicle" => "Motor Vehicle",
        "liability" => "Liability",
        "self_pay" => "Self Pay",
        _ => null
    };

    public void ApplyTo(IntakeResponseDraft draft, bool confirmedBlankFields = false)
    {
        if (draft.IsLocked || draft.IsSubmitted)
        {
            return;
        }

        bool Empty(string? value) => confirmedBlankFields ? string.IsNullOrWhiteSpace(value) : value is null;
        if (Empty(draft.FullName)) draft.FullName = FullName;
        if (Empty(draft.EmailAddress)) draft.EmailAddress = EmailAddress;
        if (Empty(draft.PhoneNumber)) draft.PhoneNumber = PhoneNumber;
        if (Empty(draft.AddressLine1)) draft.AddressLine1 = AddressLine1;
        if (Empty(draft.AddressLine2)) draft.AddressLine2 = AddressLine2;
        if (Empty(draft.City)) draft.City = City;
        if (Empty(draft.StateOrProvince)) draft.StateOrProvince = StateOrProvince;
        if (Empty(draft.PostalCode)) draft.PostalCode = PostalCode;
        if (Empty(draft.EmergencyContactName)) draft.EmergencyContactName = EmergencyContactName;
        if (Empty(draft.EmergencyContactPhone)) draft.EmergencyContactPhone = EmergencyContactPhone;
        draft.DateOfBirth ??= DateOfBirth;
        // A policy is copied as a unit; never combine one carrier with another policy's member number.
        if (!draft.OmitPrimaryPolicy && !PrimaryPolicyAmbiguous && Empty(draft.InsuranceCompanyName) && Empty(draft.MemberOrPolicyNumber) && Empty(draft.GroupNumber)
            && Empty(draft.PayerType) && Empty(draft.AdjusterName) && Empty(draft.AdjusterPhone)
            && Empty(draft.AdjusterEmail) && Empty(draft.AdjusterFax))
        {
            draft.InsuranceCompanyName = InsuranceCompanyName;
            draft.MemberOrPolicyNumber = MemberOrPolicyNumber;
            draft.GroupNumber = GroupNumber;
            draft.PayerType = PayerType;
            draft.AdjusterName = AdjusterName;
            draft.AdjusterPhone = AdjusterPhone;
            draft.AdjusterEmail = AdjusterEmail;
            draft.AdjusterFax = AdjusterFax;
            draft.PrefilledPrimaryPolicyId = PrimaryPolicyId;
            draft.PrefilledPrimaryPolicyModifiedUtc = PrimaryPolicyModifiedUtc;
        }
        if (!draft.OmitSecondaryPolicy && !SecondaryPolicyAmbiguous && Empty(draft.SecondaryInsuranceCompanyName) && Empty(draft.SecondaryMemberOrPolicyNumber)
            && Empty(draft.SecondaryGroupNumber))
        {
            draft.SecondaryInsuranceCompanyName = SecondaryInsuranceCompanyName;
            draft.SecondaryMemberOrPolicyNumber = SecondaryMemberOrPolicyNumber;
            draft.SecondaryGroupNumber = SecondaryGroupNumber;
            draft.PrefilledSecondaryPolicyId = SecondaryPolicyId;
            draft.PrefilledSecondaryPolicyModifiedUtc = SecondaryPolicyModifiedUtc;
        }
        // Do not refresh the fallback timestamp for an already accepted legacy policy source.
        draft.ProfilePrefillSourceModifiedUtc ??= SourceModifiedUtc;
    }
}
