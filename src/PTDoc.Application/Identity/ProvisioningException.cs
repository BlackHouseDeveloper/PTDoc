using System.Security.Cryptography;
using System.Text;

namespace PTDoc.Application.Identity;

public sealed class ProvisioningException : Exception
{
    public ProvisioningException(PrincipalProvisioningResult result)
        : base(result.FailureReason ?? "Authenticated principal is not provisioned.")
    {
        Provider = result.Provider;
        PrincipalType = result.PrincipalType;
        FailureCode = result.FailureCode ?? "principal_not_provisioned";
        ExternalSubjectHash = HashSubject(result.ExternalSubject);
    }

    public string FailureCode { get; }

    public string? Provider { get; }

    public string? PrincipalType { get; }

    public string? ExternalSubjectHash { get; }

    private static string? HashSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(subject));
        return Convert.ToHexString(bytes[..8]);
    }
}