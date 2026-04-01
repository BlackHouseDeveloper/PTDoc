namespace PTDoc.Api.Auth;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
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
        var claimsJson = SerializeClaims(record.Claims);
        var stored = new StoredRefreshToken
        {
            TokenHash = hash,
            Subject = record.Subject,
            ClaimsJson = claimsJson,
            ExpiresAtUtc = record.ExpiresAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.StoredRefreshTokens.Add(stored);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Handle duplicate insert due to unique TokenHash constraint
            // (e.g., a transient failure causing the caller to retry the same token issuance).
            db.Entry(stored).State = EntityState.Detached;

            var existing = await db.StoredRefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

            // If no existing record matches what we tried to store, this is a genuine conflict — rethrow.
            if (existing is null
                || existing.Subject != record.Subject
                || existing.ExpiresAtUtc != record.ExpiresAtUtc
                || existing.ClaimsJson != claimsJson)
            {
                throw;
            }

            // Existing record matches; treat as idempotent success.
        }
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

    /// <summary>
    /// Allowlist of claim types that are safe to persist alongside a refresh token.
    /// Identity claims (user ID, roles, names) are included; credential claims
    /// (e.g. <see cref="PTDocClaimTypes.ApiAccessToken"/>) are explicitly excluded
    /// to prevent credentials from being stored in the database.
    /// </summary>
    private static readonly HashSet<string> SafeRefreshClaimTypes = new(StringComparer.Ordinal)
    {
        PTDocClaimTypes.InternalUserId,
        PTDocClaimTypes.ExternalProvider,
        PTDocClaimTypes.ExternalSubject,
        PTDocClaimTypes.AuthenticationType,
        ClaimTypes.NameIdentifier,
        ClaimTypes.Name,
        ClaimTypes.Role,
        ClaimTypes.GivenName,
        ClaimTypes.Surname,
        ClaimTypes.Email,
        "sub",
        "oid",
        // Tenant and patient context claims must be preserved across refresh.
        "clinic_id",
        "patient_id",
    };

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes); // 64 upper-case hex chars
    }

    private static string SerializeClaims(IEnumerable<Claim> claims)
    {
        // Persist only known-safe identity claims.
        // Credential claims (e.g. ApiAccessToken) must never be stored in the DB.
        var dtos = claims
            .Where(c => SafeRefreshClaimTypes.Contains(c.Type))
            .Select(c => new ClaimDto { Type = c.Type, Value = c.Value });
        return JsonSerializer.Serialize(dtos);
    }

    private static IReadOnlyCollection<Claim> DeserializeClaims(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<ClaimDto>>(json) ?? new List<ClaimDto>();
        return dtos.Select(d => new Claim(d.Type, d.Value)).ToList();
    }

    /// <summary>
    /// Returns true when the exception is caused by a unique constraint violation.
    /// Checks provider-specific exception types and error codes rather than message strings
    /// for reliable cross-provider detection:
    /// - SQLite: <see cref="Microsoft.Data.Sqlite.SqliteException"/> with error code 19 (SQLITE_CONSTRAINT)
    /// - SQL Server: <see cref="Microsoft.Data.SqlClient.SqlException"/> with error number 2627 or 2601
    /// - PostgreSQL: <see cref="Npgsql.PostgresException"/> with SqlState "23505"
    /// Other <see cref="DbUpdateException"/> types (deadlock, timeout, etc.) return false and propagate.
    /// </summary>
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException switch
        {
            Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteErrorCode == 19, // SQLITE_CONSTRAINT
            Microsoft.Data.SqlClient.SqlException sql => sql.Number is 2627 or 2601,
            Npgsql.PostgresException pg => pg.SqlState == "23505",
            _ => false
        };
    }

    private sealed class ClaimDto
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
