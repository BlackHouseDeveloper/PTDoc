namespace PTDoc.Application.Auth;

public sealed record UserInfo(
    string UserId,
    string DisplayName,
    string Email,
    IReadOnlyCollection<string> Roles);