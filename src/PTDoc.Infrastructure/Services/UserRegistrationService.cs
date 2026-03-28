using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Infrastructure.Services;

public sealed class UserRegistrationService : IUserRegistrationService
{
    private static readonly IReadOnlyList<RoleSummary> RegisterableRoles =
    [
        new("PT", "Physical Therapist"),
        new("PTA", "Physical Therapist Assistant"),
        new("FrontDesk", "Front Desk"),
        new("Owner", "Owner")
    ];

    private static readonly IReadOnlySet<string> AllowedRoleKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PT", "PTA", "FrontDesk", "Owner"
        };

    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<UserRegistrationService> logger;

    public UserRegistrationService(ApplicationDbContext dbContext, ILogger<UserRegistrationService> logger)
    {
        this.dbContext = dbContext;
        this.logger = logger;
    }

    public async Task<RegistrationResult> RegisterAsync(UserRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Pin)
            || request.Pin.Length != 4
            || request.Pin.Any(static ch => !char.IsDigit(ch)))
        {
            return new RegistrationResult(RegistrationStatus.InvalidPin, null, "PIN must be 4 digits.");
        }

        if (request.ClinicId is null || request.ClinicId == Guid.Empty)
        {
            return new RegistrationResult(RegistrationStatus.ClinicNotFound, null, "Clinic is required.");
        }

        var clinicExists = await dbContext.Clinics
            .AnyAsync(c => c.Id == request.ClinicId.Value && c.IsActive, cancellationToken);

        if (!clinicExists)
        {
            return new RegistrationResult(RegistrationStatus.ClinicNotFound, null, "Selected clinic was not found.");
        }

        var normalizedRole = request.RoleKey?.Trim() ?? string.Empty;
        if (!AllowedRoleKeys.Contains(normalizedRole))
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "The selected role is not valid for registration.");
        }

        var requiresLicense = string.Equals(normalizedRole, "PT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedRole, "PTA", StringComparison.OrdinalIgnoreCase);

        if (requiresLicense
            && (string.IsNullOrWhiteSpace(request.LicenseType)
                || string.IsNullOrWhiteSpace(request.LicenseNumber)
                || string.IsNullOrWhiteSpace(request.LicenseState)))
        {
            return new RegistrationResult(RegistrationStatus.InvalidLicenseData, null, "License fields are required for PT/PTA registration.");
        }

        var normalizedEmail = request.Email.Trim();
        var emailExists = await dbContext.Users
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);

        if (emailExists)
        {
            return new RegistrationResult(RegistrationStatus.EmailAlreadyExists, null, "Email already exists.");
        }

        var username = await GenerateUniqueUsernameAsync(normalizedEmail, cancellationToken);
        if (username is null)
        {
            return new RegistrationResult(RegistrationStatus.UsernameCollision, null, "Unable to generate a unique username.");
        }

        var (firstName, lastName) = SplitName(request.FullName);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            Email = normalizedEmail,
            PinHash = AuthService.HashPin(request.Pin),
            Role = normalizedRole,
            ClinicId = request.ClinicId,
            LicenseNumber = requiresLicense ? request.LicenseNumber?.Trim() : null,
            LicenseState = requiresLicense ? request.LicenseState?.Trim().ToUpperInvariant() : null,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Pending registration created for user {UserId} ({Email})", user.Id, normalizedEmail);

        return new RegistrationResult(RegistrationStatus.PendingApproval, user.Id, null);
    }

    public async Task<IReadOnlyList<ClinicSummary>> GetActiveClinicListAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Clinics
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ClinicSummary(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<RoleSummary>> GetRegisterableRolesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(RegisterableRoles);

    public async Task<IReadOnlyList<PendingUserSummary>> GetPendingRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(u => !u.IsActive)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new PendingUserSummary(
                u.Id,
                (u.FirstName + " " + u.LastName).Trim(),
                u.Email ?? string.Empty,
                u.Role,
                u.ClinicId,
                u.Clinic != null ? u.Clinic.Name : null,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<RegistrationResult> ApproveRegistrationAsync(Guid userId, Guid approvedBy, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "User is already active and cannot be approved again.");
        }

        user.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registration approved for {UserId} by {ApprovedBy}", userId, approvedBy);
        return new RegistrationResult(RegistrationStatus.Succeeded, userId, null);
    }

    public async Task<RegistrationResult> RejectRegistrationAsync(Guid userId, Guid rejectedBy, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cannot reject an already-active user account.");
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registration rejected for {UserId} by {RejectedBy}", userId, rejectedBy);
        return new RegistrationResult(RegistrationStatus.Succeeded, userId, null);
    }

    private async Task<string?> GenerateUniqueUsernameAsync(string email, CancellationToken cancellationToken)
    {
        var emailPrefix = email.Split('@', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(emailPrefix))
        {
            emailPrefix = "user";
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = attempt == 0 ? emailPrefix : $"{emailPrefix}{attempt + 1}";
            var exists = await dbContext.Users.AnyAsync(u => u.Username == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        return null;
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var normalized = fullName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ("Unknown", "User");
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            return (parts[0], parts[0]);
        }

        var firstName = parts[0];
        var lastName = string.Join(' ', parts.Skip(1));
        return (firstName, lastName);
    }
}
