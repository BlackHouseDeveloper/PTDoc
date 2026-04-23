using System.Security.Claims;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Infrastructure.Services;

public sealed class LegacyApiCredentialValidator : ICredentialValidator
{
    private readonly ApplicationDbContext _context;

    public LegacyApiCredentialValidator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClaimsIdentity?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var normalizedUsername = username.Trim();
        var normalizedUsernameLower = normalizedUsername.ToLowerInvariant();
        var identifierCandidates = normalizedUsername.Equals(normalizedUsernameLower, StringComparison.Ordinal)
            ? [normalizedUsernameLower]
            : new[] { normalizedUsername, normalizedUsernameLower };
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive &&
                (identifierCandidates.Contains(u.Username)
                 || (u.Email != null && identifierCandidates.Contains(u.Email))))
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Role,
                u.ClinicId,
                u.PinHash
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            // Legacy fallback for rows that predate save-time identifier normalization.
            user = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive &&
                    (u.Username.ToLower() == normalizedUsernameLower
                     || (u.Email != null && u.Email.ToLower() == normalizedUsernameLower)))
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Role,
                    u.ClinicId,
                    u.PinHash
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PinHash))
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(PTDocClaimTypes.InternalUserId, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new(PTDocClaimTypes.AuthenticationType, "legacy_jwt")
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (user.ClinicId.HasValue)
        {
            claims.Add(new Claim(HttpTenantContextAccessor.ClinicIdClaimType, user.ClinicId.Value.ToString()));
        }

        var displayName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(displayName) && !claims.Any(claim => claim.Type == ClaimTypes.Name && claim.Value == displayName))
        {
            claims.Add(new Claim("display_name", displayName));
        }

        return new ClaimsIdentity(claims, "PTDocLegacyJwt");
    }
}
