namespace PTDoc.Api.Identity;

/// <summary>
/// Request model for PIN-based login
/// </summary>
public class PinLoginRequest
{
    public required string Username { get; init; }
    public required string Pin { get; init; }
}

/// <summary>
/// Response model for successful login
/// </summary>
public class PinLoginResponse
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string Role { get; init; }
}

/// <summary>
/// Response model for the /me endpoint
/// </summary>
public class CurrentUserResponse
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Role { get; init; }
    public required bool IsActive { get; init; }
}
