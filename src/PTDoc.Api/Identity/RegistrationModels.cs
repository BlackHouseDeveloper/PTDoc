namespace PTDoc.Api.Identity;

public sealed class SelfServiceRegisterRequest
{
    public required string FullName { get; init; }

    public required string Email { get; init; }

    public required DateTime DateOfBirth { get; init; }

    public required string RoleKey { get; init; }

    public Guid? ClinicId { get; init; }

    public required string Pin { get; init; }

    public string? LicenseType { get; init; }

    public string? LicenseNumber { get; init; }

    public string? LicenseState { get; init; }
}

public sealed class SelfServiceRegisterResponse
{
    public required string Status { get; init; }

    public Guid? UserId { get; init; }

    public string? Error { get; init; }
}

public sealed class ClinicListItem
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}

public sealed class RoleListItem
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }
}
