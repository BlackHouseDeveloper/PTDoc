using PTDoc.Application.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PTDoc.Application.Intake;

/// <summary>Saved demographic/policy values offered separately from intake answers.</summary>
public sealed class IntakeProfilePrefill
{
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
            || string.IsNullOrWhiteSpace(draft.InsuranceCompanyName) && string.IsNullOrWhiteSpace(draft.MemberOrPolicyNumber)
                && string.IsNullOrWhiteSpace(draft.GroupNumber) && string.IsNullOrWhiteSpace(draft.PayerType)
                && string.IsNullOrWhiteSpace(draft.AdjusterName) && string.IsNullOrWhiteSpace(draft.AdjusterPhone)
                && string.IsNullOrWhiteSpace(draft.AdjusterEmail) && string.IsNullOrWhiteSpace(draft.AdjusterFax)
                && (!string.IsNullOrWhiteSpace(InsuranceCompanyName) || !string.IsNullOrWhiteSpace(MemberOrPolicyNumber)
                    || !string.IsNullOrWhiteSpace(GroupNumber) || !string.IsNullOrWhiteSpace(PayerType)
                    || !string.IsNullOrWhiteSpace(AdjusterName) || !string.IsNullOrWhiteSpace(AdjusterPhone)
                    || !string.IsNullOrWhiteSpace(AdjusterEmail) || !string.IsNullOrWhiteSpace(AdjusterFax))
            || string.IsNullOrWhiteSpace(draft.SecondaryInsuranceCompanyName)
                && string.IsNullOrWhiteSpace(draft.SecondaryMemberOrPolicyNumber) && string.IsNullOrWhiteSpace(draft.SecondaryGroupNumber)
                && (!string.IsNullOrWhiteSpace(SecondaryInsuranceCompanyName) || !string.IsNullOrWhiteSpace(SecondaryMemberOrPolicyNumber)
                    || !string.IsNullOrWhiteSpace(SecondaryGroupNumber)));

    public string ApplyToNewResponseJson(string? responseJson)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = string.IsNullOrWhiteSpace(responseJson) ? new JsonObject() : JsonNode.Parse(responseJson) as JsonObject
            ?? throw new JsonException("Intake response must be a JSON object.");
        var draft = original.Deserialize<IntakeResponseDraft>(options) ?? new();
        draft.IsLocked = false;
        draft.IsSubmitted = false;
        ApplyTo(draft);
        var populated = JsonSerializer.SerializeToNode(draft, options)!.AsObject();
        string[] fields =
        [
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
            original[existingKey ?? field] = populated[field]?.DeepClone();
        }
        return original.ToJsonString(options);
    }

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
        if (Empty(draft.InsuranceCompanyName) && Empty(draft.MemberOrPolicyNumber) && Empty(draft.GroupNumber)
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
        if (Empty(draft.SecondaryInsuranceCompanyName) && Empty(draft.SecondaryMemberOrPolicyNumber)
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
