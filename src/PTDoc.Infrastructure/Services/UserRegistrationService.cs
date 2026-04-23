using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PTDoc.Infrastructure.Services;

public sealed class UserRegistrationService : IUserRegistrationService
{
    private const string RegistrationCreatedEventType = "RegistrationCreated";
    private const string RegistrationUpdatedEventType = "RegistrationUpdated";
    private const string RegistrationApprovedEventType = "RegistrationApproved";
    private const string RegistrationRejectedEventType = "RegistrationRejected";
    private const string RegistrationOnHoldEventType = "RegistrationOnHold";
    private const string RegistrationCancelledEventType = "RegistrationCancelled";

    private static readonly IReadOnlyList<RoleSummary> RegisterableRoles =
    [
        new(Roles.PT, "Physical Therapist"),
        new(Roles.PTA, "Physical Therapist Assistant"),
        new(Roles.FrontDesk, "Front Desk"),
        new(Roles.Owner, "Owner"),
        new(Roles.Billing, "Billing"),
        new(Roles.Patient, "Patient")
    ];

    private static readonly IReadOnlySet<string> AllowedRoleKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Roles.PT, Roles.PTA, Roles.FrontDesk, Roles.Owner, Roles.Billing, Roles.Patient
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
            return ValidationFailure("The selected role is not valid for registration.", CreateFieldError("RoleKey", "The selected role is not valid."));
        }

        var validationErrors = ValidateRegistrationData(
            request.FullName,
            request.Email,
            request.DateOfBirth,
            normalizedRole,
            request.LicenseNumber,
            request.LicenseState);
        if (validationErrors.Count > 0)
        {
            var hasOnlyLicenseErrors = validationErrors.Keys.All(static key =>
                key is "LicenseNumber" or "LicenseState");

            return new RegistrationResult(
                hasOnlyLicenseErrors ? RegistrationStatus.InvalidLicenseData : RegistrationStatus.ValidationFailed,
                null,
                hasOnlyLicenseErrors
                    ? "License fields are required for PT/PTA registration."
                    : "Registration data is incomplete.",
                validationErrors);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (!emailExists)
        {
            emailExists = await dbContext.Users
                .AnyAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail, cancellationToken);
        }

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
            DateOfBirth = request.DateOfBirth.Date,
            PinHash = AuthService.HashPin(request.Pin),
            Role = normalizedRole,
            ClinicId = request.ClinicId,
            LicenseNumber = RequiresLicense(normalizedRole) ? request.LicenseNumber?.Trim() : null,
            LicenseState = RequiresLicense(normalizedRole) ? request.LicenseState?.Trim().ToUpperInvariant() : null,
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

        var rows = await registrationQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(row => new
            {
                row.User.Id,
                row.User.FirstName,
                row.User.LastName,
                row.User.Email,
                row.User.Role,
                row.User.ClinicId,
                ClinicName = row.User.Clinic != null ? row.User.Clinic.Name : null,
                row.User.CreatedAt,
                row.User.IsActive,
                row.User.DateOfBirth,
                row.User.LicenseNumber,
                row.User.LicenseState,
                row.LastDecision
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row =>
            {
                var fullName = $"{row.FirstName} {row.LastName}".Trim();
                var missingFields = GetMissingFields(
                    fullName,
                    row.Email,
                    row.DateOfBirth,
                    row.Role,
                    row.LicenseNumber,
                    row.LicenseState);

                return new PendingUserSummary(
                    row.Id,
                    fullName,
                    row.Email ?? string.Empty,
                    GetRegistrationStatus(row.IsActive, row.LastDecision),
                    row.Role,
                    row.ClinicId,
                    row.ClinicName,
                    row.CreatedAt,
                    missingFields.Count == 0,
                    missingFields,
                    NormalizeStoredValue(row.LicenseNumber),
                    NormalizeStoredValue(row.LicenseState),
                    null);
            })
            .ToList();

        return new PendingRegistrationsPage(
            await PopulateReviewedByAsync(items, cancellationToken),
            totalCount,
            normalizedPage,
            normalizedPageSize);
    }

    public async Task<PendingUserDetail?> GetPendingRegistrationAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.Clinic)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var latestDecision = await GetLatestDecisionAsync(userId, cancellationToken);
        var reviewedBy = await ResolveReviewedByAsync(latestDecision.ActorUserId, cancellationToken);
        var missingFields = GetMissingFields(user);

        return new PendingUserDetail(
            user.Id,
            user.Username,
            BuildFullName(user),
            user.Email ?? string.Empty,
            user.DateOfBirth,
            GetRegistrationStatus(user.IsActive, latestDecision.EventType),
            user.Role,
            user.ClinicId,
            user.Clinic?.Name,
            user.CreatedAt,
            missingFields.Count == 0,
            missingFields,
            NormalizeStoredValue(user.LicenseNumber),
            NormalizeStoredValue(user.LicenseState),
            reviewedBy);
    }

    public async Task<RegistrationResult> UpdatePendingRegistrationAsync(
        Guid userId,
        AdminRegistrationUpdateRequest request,
        Guid editedBy,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(u => u.Clinic)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.NotFound, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Approved users cannot be modified via the registration workflow.");
        }

        var latestDecision = await GetLatestDecisionAsync(userId, cancellationToken);
        if (latestDecision.EventType == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be edited.");
        }

        var normalizedRole = request.RoleKey?.Trim() ?? string.Empty;
        var validationErrors = ValidateRegistrationData(
            request.FullName,
            request.Email,
            request.DateOfBirth,
            normalizedRole,
            request.LicenseNumber,
            request.LicenseState);
        if (validationErrors.Count > 0)
        {
            return ValidationFailure("Registration data is incomplete.", validationErrors, userId);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var duplicateEmail = await dbContext.Users
            .AnyAsync(
                existing => existing.Id != userId
                    && existing.Email == normalizedEmail,
                cancellationToken);

        if (!duplicateEmail)
        {
            duplicateEmail = await dbContext.Users
                .AnyAsync(
                    existing => existing.Id != userId
                        && existing.Email != null
                        && existing.Email.ToLower() == normalizedEmail,
                    cancellationToken);
        }
        if (duplicateEmail)
        {
            return ValidationFailure(
                "An account with that email already exists.",
                CreateFieldError("Email", "An account with that email already exists."),
                userId);
        }

        var changedFields = new List<string>();
        ApplyUpdatedName(request.FullName.Trim(), user, changedFields);
        ApplyUpdatedValue(normalizedEmail, user.Email, value => user.Email = value, "Email", changedFields);
        ApplyUpdatedValue((DateTime?)request.DateOfBirth!.Value.Date, user.DateOfBirth, value => user.DateOfBirth = value, "DateOfBirth", changedFields);
        ApplyUpdatedValue(normalizedRole, user.Role, value => user.Role = value, "RoleKey", changedFields);

        if (RequiresLicense(normalizedRole))
        {
            ApplyUpdatedValue(request.LicenseNumber!.Trim(), user.LicenseNumber, value => user.LicenseNumber = value, "LicenseNumber", changedFields);
            ApplyUpdatedValue(request.LicenseState!.Trim().ToUpperInvariant(), user.LicenseState, value => user.LicenseState = value, "LicenseState", changedFields);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(user.LicenseNumber))
            {
                user.LicenseNumber = null;
                AddChangedField(changedFields, "LicenseNumber");
            }

            if (!string.IsNullOrWhiteSpace(user.LicenseState))
            {
                user.LicenseState = null;
                AddChangedField(changedFields, "LicenseState");
            }
        }

        if (changedFields.Count > 0)
        {
            dbContext.AuditLogs.Add(CreateRegistrationAudit(
                eventType: RegistrationUpdatedEventType,
                actorUserId: editedBy,
                entityId: userId,
                metadata: new Dictionary<string, object?>
                {
                    ["changedFields"] = changedFields.Distinct(StringComparer.Ordinal).OrderBy(static field => field).ToArray()
                }));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Registration updated for {UserId} by {EditedBy}", userId, editedBy);
        return new RegistrationResult(RegistrationStatus.Succeeded, userId, null);
    }

    public async Task<RegistrationResult> ApproveRegistrationAsync(Guid userId, Guid approvedBy, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new RegistrationResult(RegistrationStatus.NotFound, null, "Registration not found.");
        }

        var latestDecision = await GetLatestDecisionAsync(userId, cancellationToken);
        if (latestDecision.EventType == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be approved.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "User is already active and cannot be approved again.");
        }

        var validationErrors = ValidateRegistrationData(
            BuildFullName(user),
            user.Email,
            user.DateOfBirth,
            user.Role,
            user.LicenseNumber,
            user.LicenseState);
        if (validationErrors.Count > 0)
        {
            return ValidationFailure("Registration data is incomplete.", validationErrors, userId);
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
            return new RegistrationResult(RegistrationStatus.NotFound, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cannot reject an already-active user account.");
        }

        var latestDecision = await GetLatestDecisionAsync(userId, cancellationToken);
        if (latestDecision.EventType == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be rejected.");
        }

        var alreadyRejected = latestDecision.EventType == RegistrationRejectedEventType;

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
            return new RegistrationResult(RegistrationStatus.NotFound, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Active users cannot be placed on hold.");
        }

        var latestDecision = await GetLatestDecisionAsync(userId, cancellationToken);
        if (latestDecision.EventType == RegistrationCancelledEventType)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Cancelled registrations cannot be placed on hold.");
        }

        if (latestDecision.EventType == RegistrationOnHoldEventType)
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
            return new RegistrationResult(RegistrationStatus.NotFound, null, "Registration not found.");
        }

        if (user.IsActive)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Active users cannot be cancelled via registration workflow.");
        }

        var latestDecision = await GetLatestDecisionAsync(userId, cancellationToken);
        if (latestDecision.EventType == RegistrationCancelledEventType)
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
        var emailPrefix = email.Split('@', 2)[0].Trim().ToLowerInvariant();
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

    private async Task<RegistrationDecision> GetLatestDecisionAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.AuditLogs
            .Where(a => a.EntityType == nameof(User)
                && a.EntityId == userId
                && (a.EventType == RegistrationApprovedEventType
                    || a.EventType == RegistrationRejectedEventType
                    || a.EventType == RegistrationOnHoldEventType
                    || a.EventType == RegistrationCancelledEventType))
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => new RegistrationDecision(a.EventType, a.UserId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new RegistrationDecision(null, null);
    }

    private async Task<IReadOnlyList<PendingUserSummary>> PopulateReviewedByAsync(
        IReadOnlyList<PendingUserSummary> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var itemIds = items.Select(item => item.Id).ToArray();

        var decisionLookups = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == nameof(User)
                && a.EntityId.HasValue
                && itemIds.Contains(a.EntityId.Value)
                && (a.EventType == RegistrationApprovedEventType
                    || a.EventType == RegistrationRejectedEventType
                    || a.EventType == RegistrationOnHoldEventType
                    || a.EventType == RegistrationCancelledEventType))
            .OrderByDescending(a => a.TimestampUtc)
            .ToListAsync(cancellationToken);

        var latestByEntity = decisionLookups
            .Where(entry => entry.EntityId.HasValue)
            .GroupBy(entry => entry.EntityId!.Value)
            .ToDictionary(group => group.Key, group => group.First(), EqualityComparer<Guid>.Default);

        var actorIds = latestByEntity.Values
            .Where(entry => entry.UserId.HasValue)
            .Select(entry => entry.UserId!.Value)
            .Distinct()
            .ToHashSet();

        var actorLookup = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Users
                .AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, BuildFullName, cancellationToken);

        return items
            .Select(item =>
            {
                if (!latestByEntity.TryGetValue(item.Id, out var decision) || !decision.UserId.HasValue)
                {
                    return item;
                }

                return item with
                {
                    ReviewedBy = actorLookup.TryGetValue(decision.UserId.Value, out var reviewedBy)
                        ? reviewedBy
                        : null
                };
            })
            .ToList();
    }

    private async Task<string?> ResolveReviewedByAsync(Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (!actorUserId.HasValue)
        {
            return null;
        }

        var actor = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == actorUserId.Value, cancellationToken);

        return actor is null ? null : BuildFullName(actor);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateRegistrationData(
        string? fullName,
        string? email,
        DateTime? dateOfBirth,
        string? roleKey,
        string? licenseNumber,
        string? licenseState)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["FullName"] = ["Full legal name is required."];
        }

        var normalizedEmail = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            errors["Email"] = ["Email is required."];
        }
        else if (!(new EmailAddressAttribute().IsValid(normalizedEmail)))
        {
            errors["Email"] = ["A valid email address is required."];
        }

        if (!dateOfBirth.HasValue || dateOfBirth.Value == default)
        {
            errors["DateOfBirth"] = ["Date of birth is required."];
        }

        var normalizedRole = roleKey?.Trim() ?? string.Empty;
        if (!AllowedRoleKeys.Contains(normalizedRole))
        {
            errors["RoleKey"] = ["A valid role is required."];
        }

        if (RequiresLicense(normalizedRole))
        {
            if (string.IsNullOrWhiteSpace(licenseNumber))
            {
                errors["LicenseNumber"] = ["License number is required for PT/PTA roles."];
            }

            var normalizedState = licenseState?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedState))
            {
                errors["LicenseState"] = ["License state is required for PT/PTA roles."];
            }
            else if (normalizedState.Length != 2 || normalizedState.Any(static character => !char.IsLetter(character)))
            {
                errors["LicenseState"] = ["License state must be a 2-letter code."];
            }
        }

        return errors;
    }

    private static List<string> GetMissingFields(User user) =>
        GetMissingFields(
            BuildFullName(user),
            user.Email,
            user.DateOfBirth,
            user.Role,
            user.LicenseNumber,
            user.LicenseState);

    private static List<string> GetMissingFields(
        string? fullName,
        string? email,
        DateTime? dateOfBirth,
        string? roleKey,
        string? licenseNumber,
        string? licenseState)
    {
        return ValidateRegistrationData(
                fullName,
                email,
                dateOfBirth,
                roleKey,
                licenseNumber,
                licenseState)
            .Values
            .SelectMany(static messages => messages)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string GetRegistrationStatus(bool isActive, string? latestDecision)
    {
        return latestDecision switch
        {
            RegistrationCancelledEventType => "Cancelled",
            RegistrationOnHoldEventType => "On Hold",
            RegistrationRejectedEventType => "Rejected",
            RegistrationApprovedEventType => "Approved",
            _ => isActive ? "Approved" : "Pending"
        };
    }

    private static bool RequiresLicense(string roleKey) =>
        string.Equals(roleKey, "PT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleKey, "PTA", StringComparison.OrdinalIgnoreCase);

    private static string BuildFullName(User user) =>
        $"{user.FirstName} {user.LastName}".Trim();

    private static string? NormalizeStoredValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RegistrationResult ValidationFailure(
        string error,
        IReadOnlyDictionary<string, string[]> validationErrors,
        Guid? userId = null) =>
        new(RegistrationStatus.ValidationFailed, userId, error, validationErrors);

    private static Dictionary<string, string[]> CreateFieldError(string fieldName, string message) =>
        new(StringComparer.Ordinal)
        {
            [fieldName] = [message]
        };

    private static void ApplyUpdatedName(
        string fullName,
        User user,
        ICollection<string> changedFields)
    {
        var (updatedFirstName, updatedLastName) = SplitName(fullName);
        if (!string.Equals(user.FirstName, updatedFirstName, StringComparison.Ordinal))
        {
            user.FirstName = updatedFirstName;
            AddChangedField(changedFields, "FullName");
        }

        if (!string.Equals(user.LastName, updatedLastName, StringComparison.Ordinal))
        {
            user.LastName = updatedLastName;
            AddChangedField(changedFields, "FullName");
        }
    }

    private static void ApplyUpdatedValue<T>(
        T newValue,
        T existingValue,
        Action<T> assign,
        string fieldName,
        ICollection<string> changedFields)
    {
        if (EqualityComparer<T>.Default.Equals(newValue, existingValue))
        {
            return;
        }

        assign(newValue);
        AddChangedField(changedFields, fieldName);
    }

    private static void AddChangedField(ICollection<string> changedFields, string fieldName)
    {
        if (!changedFields.Contains(fieldName))
        {
            changedFields.Add(fieldName);
        }
    }

    private static AuditLog CreateRegistrationAudit(
        string eventType,
        Guid? actorUserId,
        Guid entityId,
        IReadOnlyDictionary<string, object?>? metadata = null)
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
            MetadataJson = JsonSerializer.Serialize(metadata ?? new Dictionary<string, object?>()),
            Success = true
        };
    }

    private sealed record RegistrationDecision(string? EventType, Guid? ActorUserId);
}
