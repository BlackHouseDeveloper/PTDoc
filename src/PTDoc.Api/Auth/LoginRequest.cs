namespace PTDoc.Api.Auth;

public sealed record LoginRequest(string Username, string Password, Guid? ClinicId = null);
