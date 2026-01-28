namespace PTDoc.Application.Auth;

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType = "Bearer");