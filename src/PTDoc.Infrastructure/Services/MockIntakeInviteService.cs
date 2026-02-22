using System.Collections.Concurrent;

using PTDoc.Application.Intake;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Development-only in-memory implementation of <see cref="IIntakeInviteService"/>.
/// OTP codes are always <c>123456</c> to simplify local testing.
/// Sessions expire after 30 minutes; OTP challenges expire after 10 minutes.
/// <para>
/// This service uses instance-level (non-static) dictionaries, ensuring each DI scope
/// (per-user connection) has its own isolated state. Do not use in production.
/// </para>
/// </summary>
public sealed class MockIntakeInviteService : IIntakeInviteService
{
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ChallengeDuration = TimeSpan.FromMinutes(10);
    private const string DevOtpCode = "123456";

    private readonly ConcurrentDictionary<string, (string Contact, OtpContactType Type, DateTimeOffset Expires)> _pendingChallenges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IntakeAccessSession> _activeSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _redeemedInvites = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IntakeAccessSession?> ValidateInviteTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || _redeemedInvites.ContainsKey(token))
        {
            return Task.FromResult<IntakeAccessSession?>(null);
        }

        var session = new IntakeAccessSession(
            SessionId: Guid.NewGuid().ToString("N"),
            ExpiresAtUtc: DateTimeOffset.UtcNow.Add(SessionDuration),
            InviteToken: token);

        _activeSessions[session.SessionId] = session;
        _redeemedInvites[token] = true;

        return Task.FromResult<IntakeAccessSession?>(session);
    }

    /// <inheritdoc />
    public Task<IntakeOtpChallenge> RequestOtpAsync(string contact, OtpContactType contactType, CancellationToken cancellationToken = default)
    {
        var challengeId = Guid.NewGuid().ToString("N");
        var expiry = DateTimeOffset.UtcNow.Add(ChallengeDuration);

        _pendingChallenges[challengeId] = (contact, contactType, expiry);

        // In production this would send an SMS or email; here we use the static dev code.
        return Task.FromResult(new IntakeOtpChallenge(challengeId, expiry));
    }

    /// <inheritdoc />
    public Task<IntakeAccessSession?> VerifyOtpAsync(string challengeId, string otpCode, CancellationToken cancellationToken = default)
    {
        if (!_pendingChallenges.TryRemove(challengeId, out var challenge))
        {
            return Task.FromResult<IntakeAccessSession?>(null);
        }

        if (challenge.Expires < DateTimeOffset.UtcNow)
        {
            return Task.FromResult<IntakeAccessSession?>(null);
        }

        if (!string.Equals(otpCode, DevOtpCode, StringComparison.Ordinal))
        {
            return Task.FromResult<IntakeAccessSession?>(null);
        }

        var session = new IntakeAccessSession(
            SessionId: Guid.NewGuid().ToString("N"),
            ExpiresAtUtc: DateTimeOffset.UtcNow.Add(SessionDuration));

        _activeSessions[session.SessionId] = session;

        return Task.FromResult<IntakeAccessSession?>(session);
    }

    /// <inheritdoc />
    public Task<bool> IsSessionValidAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var valid = _activeSessions.TryGetValue(sessionId, out var session)
            && session.ExpiresAtUtc > DateTimeOffset.UtcNow;

        return Task.FromResult(valid);
    }

    /// <inheritdoc />
    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _activeSessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
