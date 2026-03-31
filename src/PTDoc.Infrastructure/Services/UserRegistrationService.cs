using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Infrastructure.Services;

public sealed class UserRegistrationService : IUserRegistrationService
{
    private const string RegistrationCreatedEventType = "RegistrationCreated";
    private const string RegistrationApprovedEventType = "RegistrationApproved";
    private const string RegistrationRejectedEventType = "RegistrationRejected";
    private const string RegistrationOnHoldEventType = "RegistrationOnHold";
    private const string RegistrationCancelledEventType = "RegistrationCancelled";

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
        dbContext.AuditLogs.Add(CreateRegistrationAudit(
            eventType: RegistrationCreatedEventType,
            actorUserId: null,
            entityId: user.Id));

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Pending registration created for user {UserId}", user.Id);

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

    public async Task<PendingRegistrationsPage> GetPendingRegistrationsAsync(
        PendingRegistrationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedPageSize = query.PageSize is 10 or 25 or 50 ? query.PageSize : 25;
        var normalizedPage = query.Page < 1 ? 1 : query.Page;

        var registrationQuery = dbContext.Users
            .AsNoTracking()
            .Where(u => u.Role != "System")
            .Select(u => new
            {
                User = u,
                LastDecision = dbContext.AuditLogs
                    .Where(a => a.EntityType == nameof(User)
                        && a.EntityId == u.Id
                        && (a.EventType == RegistrationApprovedEventType
                            || a.EventType == RegistrationRejectedEventType
                            || a.EventType == RegistrationOnHoldEventType
                            || a.EventType == RegistrationCancelledEventType))
                    .OrderByDescending(a => a.TimestampUtc)
                    .Select(a => a.EventType)
                    .FirstOrDefault()
            });

        var normalizedStatus = query.Status?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedStatus) && !string.Equals(normalizedStatus, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(normalizedStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                registrationQuery = registrationQuery.Where(row =>
                    !row.User.IsActive
                    && row.LastDecision != RegistrationRejectedEventType);
            }
            else if (string.Equals(normalizedStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                registrationQuery = registrationQuery.Where(row =>
                    row.User.IsActive
                    && row.LastDecision == RegistrationApprovedEventType);
            }
            else if (string.Equals(normalizedStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                registrationQuery = registrationQuery.Where(row =>
                    !row.User.IsActive
                    && row.LastDecision == RegistrationRejectedEventType);
            }
            else if (string.Equals(normalizedStatus, "On Hold", StringComparison.OrdinalIgnoreCase))
            {
                registrationQuery = registrationQuery.Where(row =>
                    !row.User.IsActive
                    && row.LastDecision == RegistrationOnHoldEventType);
            }
            else if (string.Equals(normalizedStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                registrationQuery = registrationQuery.Where(row =>
                    !row.User.IsActive
                    && row.LastDecision == RegistrationCancelledEventType);
            }
            else
            {
                registrationQuery = registrationQuery.Where(_ => false);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            registrationQuery = registrationQuery.Where(row =>
                ((row.User.FirstName + " " + row.User.LastName).Trim()).Contains(search)
                || (row.User.Email ?? string.Empty).Contains(search)
                || row.User.Id.ToString().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Role) && !string.Equals(query.Role, "All Roles", StringComparison.OrdinalIgnoreCase))
        {
            var role = query.Role.Trim();
            registrationQuery = registrationQuery.Where(row => row.User.Role == role);
        }

        if (!string.IsNullOrWhiteSpace(query.Clinic) && !string.Equals(query.Clinic, "All Clinics", StringComparison.OrdinalIgnoreCase))
        {
            var clinic = query.Clinic.Trim();
            registrationQuery = registrationQuery.Where(row => row.User.Clinic != null && row.User.Clinic.Name == clinic);
        }

        if (query.FromDate.HasValue)
        {
            var fromUtc = query.FromDate.Value.Date;
            registrationQuery = registrationQuery.Where(row => row.User.CreatedAt >= fromUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusiveUtc = query.ToDate.Value.Date.AddDays(1);
            registrationQuery = registrationQuery.Where(row => row.User.CreatedAt < toExclusiveUtc);
        }

        registrationQuery = (query.SortBy ?? string.Empty) switch
        {
            "Name" => registrationQuery.OrderBy(row => row.User.FirstName).ThenBy(row => row.User.LastName),
            "Status" => registrationQuery.OrderBy(row => row.LastDecision).ThenBy(row => row.User.IsActive),
            _ => registrationQuery.OrderByDescending(row => row.User.CreatedAt)
        };

        var totalCount = await registrationQuery.CountAsync(cancellationToken);

        var items = await registrationQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(row => new PendingUserSummary(
                row.User.Id,
                (row.User.FirstName + " " + row.User.LastName).Trim(),
                row.User.Email ?? string.Empty,
                row.LastDecision == RegistrationCancelledEventType
                    ? "Cancelled"
                    : row.LastDecision == RegistrationOnHoldEventType
                        ? "On Hold"
                        : row.LastDecision == RegistrationRejectedEventType
                            ? "Rejected"
                            : (row.User.IsActive || row.LastDecision == RegistrationApprovedEventType ? "Approved" : "Pending"),
                row.User.Role,
                row.User.ClinicId,
                row.User.Clinic != null ? row.User.Clinic.Name : null,
                row.User.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PendingRegistrationsPage(items, totalCount, normalizedPage, normalizedPageSize);
    }

    public async Task<RegistrationResult> ApproveRegistrationAsync(Guid userId, Guid approvedBy, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration not found.");
        }

        var latestDecision = await GetLatestDecisionEventAsync(userId, cancellationToken);
        if (latestDecision == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be approved.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "User is already active and cannot be approved again.");
        }

        user.IsActive = true;
        dbContext.AuditLogs.Add(CreateRegistrationAudit(
            eventType: RegistrationApprovedEventType,
            actorUserId: approvedBy,
            entityId: userId));

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

        var latestDecision = await GetLatestDecisionEventAsync(userId, cancellationToken);
        if (latestDecision == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be rejected.");
        }

        var alreadyRejected = latestDecision == RegistrationRejectedEventType;

        if (alreadyRejected)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration is already rejected.");
        }

        dbContext.AuditLogs.Add(CreateRegistrationAudit(
            eventType: RegistrationRejectedEventType,
            actorUserId: rejectedBy,
            entityId: userId));

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registration rejected for {UserId} by {RejectedBy}", userId, rejectedBy);
        return new RegistrationResult(RegistrationStatus.Succeeded, userId, null);
    }

    public async Task<RegistrationResult> HoldRegistrationAsync(Guid userId, Guid heldBy, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Active users cannot be placed on hold.");
        }

        var latestDecision = await GetLatestDecisionEventAsync(userId, cancellationToken);
        if (latestDecision == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be placed on hold.");
        }

        if (latestDecision == RegistrationOnHoldEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration is already on hold.");
        }

        dbContext.AuditLogs.Add(CreateRegistrationAudit(
            eventType: RegistrationOnHoldEventType,
            actorUserId: heldBy,
            entityId: userId));

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registration placed on hold for {UserId} by {HeldBy}", userId, heldBy);
        return new RegistrationResult(RegistrationStatus.Succeeded, userId, null);
    }

    public async Task<RegistrationResult> CancelRegistrationAsync(Guid userId, Guid cancelledBy, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Active users cannot be cancelled via registration workflow.");
        }

        var latestDecision = await GetLatestDecisionEventAsync(userId, cancellationToken);
        if (latestDecision == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration is already cancelled.");
        }

        dbContext.AuditLogs.Add(CreateRegistrationAudit(
            eventType: RegistrationCancelledEventType,
            actorUserId: cancelledBy,
            entityId: userId));

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registration cancelled for {UserId} by {CancelledBy}", userId, cancelledBy);
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

    private Task<string?> GetLatestDecisionEventAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.AuditLogs
            .Where(a => a.EntityType == nameof(User)
                && a.EntityId == userId
                && (a.EventType == RegistrationApprovedEventType
                    || a.EventType == RegistrationRejectedEventType
                    || a.EventType == RegistrationOnHoldEventType
                    || a.EventType == RegistrationCancelledEventType))
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => a.EventType)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static AuditLog CreateRegistrationAudit(string eventType, Guid? actorUserId, Guid entityId)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            EventType = eventType,
            Severity = "Info",
            UserId = actorUserId,
            EntityType = nameof(User),
            EntityId = entityId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            MetadataJson = "{}",
            Success = true
        };
    }
}
