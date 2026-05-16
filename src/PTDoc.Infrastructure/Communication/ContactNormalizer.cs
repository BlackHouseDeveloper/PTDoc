using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class ContactNormalizer : IContactNormalizer
{
    public ContactNormalizationResult NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ContactNormalizationResult.Failure("Email address is required.");
        }

        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
        {
            return ContactNormalizationResult.Failure("Enter a valid email address.");
        }

        return ContactNormalizationResult.Success(trimmed.ToLowerInvariant());
    }

    public ContactNormalizationResult NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ContactNormalizationResult.Failure("Mobile number is required.");
        }

        var trimmed = value.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (trimmed.StartsWith("+", StringComparison.Ordinal))
        {
            return IsValidE164Digits(digits)
                ? ContactNormalizationResult.Success($"+{digits}")
                : ContactNormalizationResult.Failure("Enter a valid mobile number.");
        }

        if (digits.Length == 10)
        {
            return ContactNormalizationResult.Success($"+1{digits}");
        }

        if (digits.Length == 11 && digits[0] == '1')
        {
            return ContactNormalizationResult.Success($"+{digits}");
        }

        return ContactNormalizationResult.Failure("Enter a valid mobile number.");
    }

    public ContactNormalizationResult NormalizeRecipient(string? value, DeliveryChannel channel)
        => channel == DeliveryChannel.Email
            ? NormalizeEmail(value)
            : NormalizePhone(value);

    public ContactNormalizationResult NormalizeAnyRecipient(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ContactNormalizationResult.Failure("A contact method is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Contains('@', StringComparison.Ordinal)
            ? NormalizeEmail(trimmed)
            : NormalizePhone(trimmed);
    }

    private static bool IsValidE164Digits(string digits)
        => digits.Length is >= 8 and <= 15 && digits[0] != '0';
}
