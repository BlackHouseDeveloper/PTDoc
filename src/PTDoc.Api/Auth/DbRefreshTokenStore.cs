namespace PTDoc.Api.Auth;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

/// <summary>
/// Database-backed refresh token store.
/// Raw token values are never persisted — only their SHA-256 hex hash.
/// Expired and revoked tokens are rejected at retrieval time without DB writes.
/// HIPAA: All token records are retained (soft revocation) to support audit trails.
/// </summary>
public sealed class DbRefreshTokenStore : IRefreshTokenStore
{
    private readonly ApplicationDbContext db;

    public DbRefreshTokenStore(ApplicationDbContext db) => this.db = db;

    public async Task StoreAsync(string refreshToken, RefreshTokenRecord record, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(refreshToken);
        var stored = new StoredRefreshToken
        {
            TokenHash = hash,
            Subject = record.Subject,
            ClaimsJson = SerializeClaims(record.Claims),
            ExpiresAtUtc = record.ExpiresAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.StoredRefreshTokens.Add(stored);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshTokenRecord?> GetAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(refreshToken);

        var stored = await db.StoredRefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.IsRevoked || stored.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return null;

        var claims = DeserializeClaims(stored.ClaimsJson);
        return new RefreshTokenRecord(stored.Subject, claims, stored.ExpiresAtUtc);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(refreshToken);

        var stored = await db.StoredRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.IsRevoked)
            return;

        stored.IsRevoked = true;
        stored.RevokedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes); // 64 upper-case hex chars
    }

    private static string SerializeClaims(IEnumerable<Claim> claims)
    {
        var dtos = claims.Select(c => new ClaimDto { Type = c.Type, Value = c.Value });
        return JsonSerializer.Serialize(dtos);
    }

    private static IReadOnlyCollection<Claim> DeserializeClaims(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<ClaimDto>>(json) ?? new List<ClaimDto>();
        return dtos.Select(d => new Claim(d.Type, d.Value)).ToList();
    }

    private sealed class ClaimDto
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
