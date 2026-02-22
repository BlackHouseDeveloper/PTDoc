namespace PTDoc.UI.Services;

/// <summary>Represents a short-lived intake session access token stored on the client.</summary>
public record IntakeSessionToken(string Token, DateTimeOffset ExpiresAt);
