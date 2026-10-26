using System.Security.Cryptography;
using System.Text;

namespace PTDoc.Infrastructure.Identity;

/// <summary>Opaque generation binding; changing the salted PIN hash invalidates prior tokens.</summary>
public static class PinCredentialBinding
{
    public static string Create(string pinHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pinHash)));
}
