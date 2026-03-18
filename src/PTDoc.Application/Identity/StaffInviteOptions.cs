namespace PTDoc.Application.Identity;

/// <summary>
/// Configuration options for staff invite and onboarding tokens.
/// </summary>
public sealed class StaffInviteOptions
{
    public const string SectionName = "StaffInvite";

    public string SigningKey { get; set; } = string.Empty;

    public int InviteExpiryHours { get; set; } = 72;

    public int AcceptanceTokenExpiryMinutes { get; set; } = 30;
}