namespace PTDoc.Application.Identity;

public static class PrincipalTypes
{
    public const string User = "User";
    public const string Patient = "Patient";
}

public sealed class PrincipalProvisioningResult
{
    public static PrincipalProvisioningResult Unauthenticated { get; } = new()
    {
        IsAuthenticated = false,
        IsProvisioned = false,
        FailureCode = "unauthenticated"
    };

    public bool IsAuthenticated { get; init; }

    public bool IsProvisioned { get; init; }

    public string? PrincipalType { get; init; }

    public string? Provider { get; init; }

    public string? ExternalSubject { get; init; }

    public Guid? InternalUserId { get; init; }

    public Guid? PatientId { get; init; }

    public Guid? ClinicId { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureReason { get; init; }

    public bool RequiresProvisioningFailure => IsAuthenticated && !IsProvisioned;
}