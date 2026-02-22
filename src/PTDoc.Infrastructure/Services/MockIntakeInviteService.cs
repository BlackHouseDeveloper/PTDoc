using System.Collections.Concurrent;
using System.Security.Cryptography;
using PTDoc.Application.Intake;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Development and test mock for <see cref="IIntakeInviteService"/>.
/// All invite tokens are accepted; the OTP is always <c>123456</c>.
/// </summary>
public sealed class MockIntakeInviteService : IIntakeInviteService
{
    private readonly ConcurrentDictionary<string, (string Contact, DateTimeOffset Expiry)> _accessTokens = new();
    private readonly ConcurrentDictionary<string, (string Otp, DateTimeOffset Expiry)> _pendingOtps = new();

    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

    public Task<IntakeInviteResult> ValidateInviteTokenAsync(string inviteToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inviteToken))
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Invalid invite token."));
        }

        var accessToken = GenerateToken();
        var expiry = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);
        _accessTokens[accessToken] = ("invite-user", expiry);

        return Task.FromResult(new IntakeInviteResult(true, accessToken, expiry, null));
    }

    public Task<bool> SendOtpAsync(string contact, OtpChannel channel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return Task.FromResult(false);
        }

        // Mock: OTP is always "123456".
        _pendingOtps[contact] = ("123456", DateTimeOffset.UtcNow.Add(OtpLifetime));
        return Task.FromResult(true);
    }

    public Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(
        string contact,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        if (!_pendingOtps.TryGetValue(contact, out var pending) || pending.Expiry < DateTimeOffset.UtcNow)
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Invalid or expired code."));
        }

        if (!string.Equals(pending.Otp, otpCode, StringComparison.Ordinal))
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Incorrect code. Please try again."));
        }

        _pendingOtps.TryRemove(contact, out _);

        var accessToken = GenerateToken();
        var expiry = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);
        _accessTokens[accessToken] = (contact, expiry);

        return Task.FromResult(new IntakeInviteResult(true, accessToken, expiry, null));
    }

    public Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (_accessTokens.TryGetValue(accessToken, out var record))
        {
            return Task.FromResult(record.Expiry > DateTimeOffset.UtcNow);
        }

        return Task.FromResult(false);
    }

    public Task RevokeAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        _accessTokens.TryRemove(accessToken, out _);
        return Task.CompletedTask;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }
}
