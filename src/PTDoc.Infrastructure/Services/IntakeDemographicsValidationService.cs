using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

using PTDoc.Core.Services;

namespace PTDoc.Infrastructure.Services;

public sealed partial class IntakeDemographicsValidationService : IIntakeDemographicsValidationService
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public DemographicsValidationResult Validate(
        string? fullName,
        DateTime? dateOfBirth,
        string? emailAddress,
        string? phoneNumber,
        string? emergencyContactName,
        string? emergencyContactPhone)
    {
        Console.WriteLine("[Intake][ValidationService] Validate called");
        Console.WriteLine($"[Intake][ValidationService] Raw => FullName='{fullName}', DOB='{dateOfBirth:O}', Email='{emailAddress}', Phone='{phoneNumber}', EmergencyName='{emergencyContactName}', EmergencyPhone='{emergencyContactPhone}'");

        var fieldErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalizedFullName = fullName?.Trim();
        var normalizedEmailAddress = emailAddress?.Trim();
        var normalizedEmergencyContactName = emergencyContactName?.Trim();
        var normalizedPhoneDigits = ToPhoneDigits(phoneNumber);
        var normalizedEmergencyPhoneDigits = ToPhoneDigits(emergencyContactPhone);

        Console.WriteLine($"[Intake][ValidationService] Normalized => FullName='{normalizedFullName}', Email='{normalizedEmailAddress}', PhoneDigits='{normalizedPhoneDigits}', EmergencyName='{normalizedEmergencyContactName}', EmergencyPhoneDigits='{normalizedEmergencyPhoneDigits}'");

        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            fieldErrors["FullName"] = "Full name is required.";
        }
        else if (!FullNameRegex().IsMatch(normalizedFullName))
        {
            fieldErrors["FullName"] = "Enter at least first and last name.";
        }

        if (!dateOfBirth.HasValue)
        {
            fieldErrors["DateOfBirth"] = "Date of birth is required.";
        }
        else
        {
            var today = DateTime.UtcNow.Date;
            var dob = dateOfBirth.Value.Date;

            if (dob > today)
            {
                fieldErrors["DateOfBirth"] = "Date of birth cannot be in the future.";
            }
            else if (dob < today.AddYears(-130))
            {
                fieldErrors["DateOfBirth"] = "Enter a valid date of birth.";
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedEmailAddress))
        {
            fieldErrors["EmailAddress"] = "Email address is required.";
        }
        else if (!EmailValidator.IsValid(normalizedEmailAddress))
        {
            fieldErrors["EmailAddress"] = "Enter a valid email address.";
        }

        if (string.IsNullOrWhiteSpace(normalizedPhoneDigits))
        {
            fieldErrors["PhoneNumber"] = "Phone number is required.";
        }
        else if (!PhoneRegex().IsMatch(normalizedPhoneDigits))
        {
            fieldErrors["PhoneNumber"] = "Enter a valid phone number.";
        }

        if (!string.IsNullOrWhiteSpace(normalizedEmergencyPhoneDigits) && !PhoneRegex().IsMatch(normalizedEmergencyPhoneDigits))
        {
            fieldErrors["EmergencyContactPhone"] = "Enter a valid emergency contact phone number.";
        }

        if (fieldErrors.Count == 0)
        {
            Console.WriteLine("[Intake][ValidationService] Validation PASS");
            return new DemographicsValidationResult();
        }

        Console.WriteLine($"[Intake][ValidationService] Validation FAIL => {string.Join(", ", fieldErrors.Select(pair => $"{pair.Key}:{pair.Value}"))}");

        return new DemographicsValidationResult
        {
            FieldErrors = fieldErrors,
            SummaryMessage = "Please correct the highlighted demographics fields before continuing."
        };
    }

    private static string ToPhoneDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;

        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                buffer[index] = character;
                index++;
            }
        }

        if (index == 11 && buffer[0] == '1')
        {
            return new string(buffer.Slice(1, 10));
        }

        return new string(buffer.Slice(0, index));
    }

    [GeneratedRegex("^\\s*[\\p{L}][\\p{L}'\\-\\.\\s]*\\s+[\\p{L}][\\p{L}'\\-\\.\\s]*$")]
    private static partial Regex FullNameRegex();

    [GeneratedRegex("^\\d{10}$")]
    private static partial Regex PhoneRegex();
}
