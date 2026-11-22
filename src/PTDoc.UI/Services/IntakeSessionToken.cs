using System.Security.Cryptography;
using System.Text;

namespace PTDoc.UI.Services;

/// <summary>Represents a short-lived intake session access token stored on the client.</summary>
public record IntakeSessionToken(string Token, DateTimeOffset ExpiresAt, string? InviteHash = null)
{
    public static IntakeSessionToken FromVerifiedInvite(string token, DateTimeOffset expiresAt, string invite)
        => new(token, expiresAt, HashInvite(invite));

    public bool MatchesInvite(string? invite) =>
        !string.IsNullOrEmpty(InviteHash) && (invite is null || InviteHash == HashInvite(invite));

    private static string HashInvite(string invite)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invite)));
}
