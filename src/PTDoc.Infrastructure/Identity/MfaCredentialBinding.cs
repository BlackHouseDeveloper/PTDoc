using System.Security.Cryptography;
using System.Text;

namespace PTDoc.Infrastructure.Identity;

/// <summary>Identifies the exact encrypted MFA credential generation without exposing its secret.</summary>
public static class MfaCredentialBinding
{
    public static string Create(Guid credentialId, string encryptedSecret) =>
        $"{credentialId:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encryptedSecret)))}";
}
