using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Settings;

public sealed class AutoCheckInAdministrationService(
    ApplicationDbContext context,
    IAuditService auditService) : IAutoCheckInAdministrationService
{
    public async Task<AutoCheckInPolicyDto> GetAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var policy = await context.AutoCheckInPolicies
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        return policy is null ? Defaults() : Map(policy);
    }

    public async Task<SettingsOperationResult<AutoCheckInPolicyDto>> UpdateAsync(
        Guid clinicId,
        UpdateAutoCheckInPolicyRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.LeadHours is < 1 or > 168)
            errors["leadHours"] = ["Lead time must be between 1 and 168 hours."];
        if (request.IsEnabled && !request.EnableEmail && !request.EnableSms)
            errors["channels"] = ["At least one consented delivery channel is required when Auto Check-In is enabled."];
        if (string.IsNullOrWhiteSpace(request.TemplateKey) || request.TemplateKey.Trim().Length > 100)
            errors["templateKey"] = ["A template key of 100 characters or fewer is required."];
        if (request.MaxAttempts is < 1 or > 10)
            errors["maxAttempts"] = ["Retry attempts must be between 1 and 10."];

        var distinctVisitTypeIds = request.EligibleVisitTypeIds.Distinct().ToArray();
        var validVisitTypes = await context.VisitTypes
            .Where(item => item.ClinicId == clinicId && distinctVisitTypeIds.Contains(item.Id) && item.IsActive && item.RequiresIntake)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (validVisitTypes.Count != distinctVisitTypeIds.Length)
            errors["eligibleVisitTypeIds"] = ["Eligible visit types must be active, clinic-owned, and require intake."];

        if (errors.Count > 0)
            return SettingsOperationResult<AutoCheckInPolicyDto>.Validation(errors);

        var policy = await context.AutoCheckInPolicies
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        if ((policy?.Version ?? 0) != request.ExpectedVersion)
            return SettingsOperationResult<AutoCheckInPolicyDto>.Conflict();

        if (policy is null)
        {
            policy = new AutoCheckInPolicy { ClinicId = clinicId, UpdatedByUserId = actorUserId };
            context.AutoCheckInPolicies.Add(policy);
        }
        else
        {
            policy.Version++;
            policy.UpdatedAtUtc = DateTime.UtcNow;
            policy.UpdatedByUserId = actorUserId;
        }

        policy.IsEnabled = request.IsEnabled;
        policy.LeadHours = request.LeadHours;
        policy.EnableEmail = request.EnableEmail;
        policy.EnableSms = request.EnableSms;
        policy.TemplateKey = request.TemplateKey.Trim();
        policy.MaxAttempts = request.MaxAttempts;
        policy.EligibleVisitTypeIdsJson = JsonSerializer.Serialize(distinctVisitTypeIds);

        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return SettingsOperationResult<AutoCheckInPolicyDto>.Conflict(); }

        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = "AutoCheckInPolicyUpdated",
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = nameof(AutoCheckInPolicy),
            EntityId = policy.Id,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = clinicId,
                ["version"] = policy.Version,
                ["eligibleVisitTypeIds"] = distinctVisitTypeIds
            }
        }, cancellationToken);
        return SettingsOperationResult<AutoCheckInPolicyDto>.Success(Map(policy));
    }

    private static AutoCheckInPolicyDto Defaults() => new(false, 24, true, true, "default-intake-invite", 3, [], 0);

    private static AutoCheckInPolicyDto Map(AutoCheckInPolicy policy)
    {
        IReadOnlyList<Guid> ids;
        try { ids = JsonSerializer.Deserialize<Guid[]>(policy.EligibleVisitTypeIdsJson) ?? []; }
        catch (JsonException) { ids = []; }
        return new AutoCheckInPolicyDto(
            policy.IsEnabled, policy.LeadHours, policy.EnableEmail, policy.EnableSms,
            policy.TemplateKey, policy.MaxAttempts, ids, policy.Version);
    }
}

public sealed class KioskCheckInService(
    ApplicationDbContext context,
    IAuditService auditService,
    IAppointmentCheckInWorkflow appointmentCheckInWorkflow) : IKioskCheckInService
{
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CheckInTokenLifetime = TimeSpan.FromHours(12);

    public async Task<IReadOnlyList<KioskStationDto>> GetStationsAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        await context.KioskStations
            .Where(item => item.ClinicId == clinicId)
            .OrderBy(item => item.Name)
            .Select(item => new KioskStationDto(item.Id, item.Name, item.IsActive, item.LastSeenAtUtc, item.Version))
            .ToListAsync(cancellationToken);

    public async Task<SettingsOperationResult<KioskEnrollmentCodeDto>> CreateStationAsync(
        Guid clinicId,
        CreateKioskStationRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (name.Length is 0 or > 120)
            return SettingsOperationResult<KioskEnrollmentCodeDto>.Validation(
                new Dictionary<string, string[]> { ["name"] = ["Station name is required and cannot exceed 120 characters."] });
        if (await context.KioskStations.AnyAsync(item => item.ClinicId == clinicId && item.Name == name, cancellationToken))
            return SettingsOperationResult<KioskEnrollmentCodeDto>.Validation(
                new Dictionary<string, string[]> { ["name"] = ["A kiosk station with this name already exists."] });

        var station = new KioskStation
        {
            ClinicId = clinicId,
            Name = name,
            DeviceCredentialHash = "pending-enrollment",
            UpdatedByUserId = actorUserId
        };
        context.KioskStations.Add(station);
        var enrollment = CreateEnrollmentCode(station);
        context.KioskEnrollmentCodes.Add(enrollment.Entity);
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "KioskStationCreated", clinicId, nameof(KioskStation), station.Id,
            actorUserId, correlationId, cancellationToken);
        return SettingsOperationResult<KioskEnrollmentCodeDto>.Success(
            new KioskEnrollmentCodeDto(station.Id, enrollment.PlainText, enrollment.Entity.ExpiresAtUtc));
    }

    public async Task<SettingsOperationResult<KioskStationDto>> UpdateStationAsync(
        Guid clinicId,
        Guid stationId,
        UpdateKioskStationRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var station = await context.KioskStations.SingleOrDefaultAsync(
            item => item.Id == stationId && item.ClinicId == clinicId, cancellationToken);
        if (station is null) return SettingsOperationResult<KioskStationDto>.NotFound();
        if (station.Version != request.ExpectedVersion) return SettingsOperationResult<KioskStationDto>.Conflict();
        var name = request.Name.Trim();
        if (name.Length is 0 or > 120)
            return SettingsOperationResult<KioskStationDto>.Validation(
                new Dictionary<string, string[]> { ["name"] = ["Station name is required and cannot exceed 120 characters."] });
        if (await context.KioskStations.AnyAsync(
                item => item.ClinicId == clinicId && item.Id != stationId && item.Name == name,
                cancellationToken))
            return SettingsOperationResult<KioskStationDto>.Validation(
                new Dictionary<string, string[]> { ["name"] = ["A kiosk station with this name already exists."] });

        station.Name = name;
        station.IsActive = request.IsActive;
        station.Version++;
        station.UpdatedByUserId = actorUserId;
        station.UpdatedAtUtc = DateTime.UtcNow;
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return SettingsOperationResult<KioskStationDto>.Conflict(); }
        await AuditAsync(
            "KioskStationUpdated", clinicId, nameof(KioskStation), station.Id,
            actorUserId, correlationId, cancellationToken);
        return SettingsOperationResult<KioskStationDto>.Success(MapStation(station));
    }

    public async Task<SettingsOperationResult<KioskEnrollmentCodeDto>> RotateEnrollmentAsync(
        Guid clinicId,
        Guid stationId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var station = await context.KioskStations.SingleOrDefaultAsync(
            item => item.Id == stationId && item.ClinicId == clinicId, cancellationToken);
        if (station is null) return SettingsOperationResult<KioskEnrollmentCodeDto>.NotFound();
        if (station.Version != expectedVersion) return SettingsOperationResult<KioskEnrollmentCodeDto>.Conflict();

        var existingCodes = await context.KioskEnrollmentCodes
            .Where(item => item.KioskStationId == station.Id && item.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var code in existingCodes) code.ConsumedAtUtc = DateTime.UtcNow;
        station.DeviceCredentialHash = "pending-enrollment";
        station.IsActive = true;
        station.Version++;
        station.UpdatedByUserId = actorUserId;
        station.UpdatedAtUtc = DateTime.UtcNow;
        var enrollment = CreateEnrollmentCode(station);
        context.KioskEnrollmentCodes.Add(enrollment.Entity);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return SettingsOperationResult<KioskEnrollmentCodeDto>.Conflict(); }
        await AuditAsync(
            "KioskEnrollmentRotated", clinicId, nameof(KioskStation), station.Id,
            actorUserId, correlationId, cancellationToken);
        return SettingsOperationResult<KioskEnrollmentCodeDto>.Success(
            new KioskEnrollmentCodeDto(station.Id, enrollment.PlainText, enrollment.Entity.ExpiresAtUtc));
    }

    public async Task<SettingsOperationResult<bool>> RevokeStationAsync(
        Guid clinicId,
        Guid stationId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var station = await context.KioskStations.SingleOrDefaultAsync(
            item => item.Id == stationId && item.ClinicId == clinicId, cancellationToken);
        if (station is null) return SettingsOperationResult<bool>.NotFound();
        if (station.Version != expectedVersion) return SettingsOperationResult<bool>.Conflict();
        station.IsActive = false;
        station.RevokedAtUtc = DateTime.UtcNow;
        station.DeviceCredentialHash = "revoked";
        station.Version++;
        station.UpdatedByUserId = actorUserId;
        station.UpdatedAtUtc = DateTime.UtcNow;
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return SettingsOperationResult<bool>.Conflict(); }
        await AuditAsync(
            "KioskStationRevoked", clinicId, nameof(KioskStation), station.Id,
            actorUserId, correlationId, cancellationToken);
        return SettingsOperationResult<bool>.Success(true);
    }

    public async Task<SettingsOperationResult<KioskEnrollmentResult>> EnrollAsync(
        string enrollmentCode,
        CancellationToken cancellationToken = default)
    {
        if (!TrySplitToken(enrollmentCode, out var codeId, out var secret))
            return SettingsOperationResult<KioskEnrollmentResult>.NotFound();
        var code = await context.KioskEnrollmentCodes
            .Include(item => item.KioskStation)
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == codeId, cancellationToken);
        if (code?.KioskStation is null || code.ConsumedAtUtc is not null || code.ExpiresAtUtc <= DateTime.UtcNow ||
            !BCrypt.Net.BCrypt.Verify(secret, code.CodeHash))
            return SettingsOperationResult<KioskEnrollmentResult>.NotFound();

        var deviceCredential = BuildSecretToken(code.KioskStation.Id, 32);
        code.KioskStation.DeviceCredentialHash = Hash(deviceCredential);
        code.KioskStation.IsActive = true;
        code.KioskStation.LastSeenAtUtc = DateTime.UtcNow;
        code.KioskStation.Version++;
        code.KioskStation.UpdatedAtUtc = DateTime.UtcNow;
        code.ConsumedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return SettingsOperationResult<KioskEnrollmentResult>.Success(
            new KioskEnrollmentResult(code.KioskStation.Id, code.KioskStation.Name, deviceCredential));
    }

    public async Task<SettingsOperationResult<KioskCheckInTokenDto>> CreateCheckInTokenAsync(
        Guid clinicId,
        Guid appointmentId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await context.Appointments.SingleOrDefaultAsync(
            item => item.Id == appointmentId && item.ClinicId == clinicId, cancellationToken);
        if (appointment is null) return SettingsOperationResult<KioskCheckInTokenDto>.NotFound();
        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Completed)
            return SettingsOperationResult<KioskCheckInTokenDto>.Validation(
                new Dictionary<string, string[]> { ["appointmentId"] = ["This appointment is not eligible for kiosk check-in."] });

        var numericCode = await CreateUniqueNumericCodeAsync(cancellationToken);
        var token = new KioskCheckInToken
        {
            ClinicId = clinicId,
            AppointmentId = appointmentId,
            TokenHash = Hash(numericCode),
            ExpiresAtUtc = DateTime.UtcNow.Add(CheckInTokenLifetime)
        };
        context.KioskCheckInTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
        var payload = $"{token.Id:N}.{numericCode}";
        await AuditAsync(
            "KioskCheckInTokenCreated", clinicId, nameof(KioskCheckInToken), token.Id,
            actorUserId, correlationId, cancellationToken,
            new Dictionary<string, object> { ["appointmentId"] = appointmentId });
        return SettingsOperationResult<KioskCheckInTokenDto>.Success(
            new KioskCheckInTokenDto(appointmentId, numericCode, payload, token.ExpiresAtUtc));
    }

    public async Task<SettingsOperationResult<KioskCheckInResult>> CheckInAsync(
        string deviceCredential,
        string appointmentToken,
        CancellationToken cancellationToken = default)
    {
        if (!TrySplitToken(deviceCredential, out var stationId, out _))
            return SettingsOperationResult<KioskCheckInResult>.NotFound();

        var station = await context.KioskStations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == stationId && item.IsActive, cancellationToken);
        if (station is null || !FixedTimeEquals(station.DeviceCredentialHash, Hash(deviceCredential)))
            return SettingsOperationResult<KioskCheckInResult>.NotFound();

        var suppliedToken = appointmentToken.Trim();
        KioskCheckInToken? token;
        string tokenSecret;
        if (TrySplitToken(suppliedToken, out var tokenId, out tokenSecret))
        {
            token = await context.KioskCheckInTokens
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.Id == tokenId && item.ClinicId == station.ClinicId, cancellationToken);
        }
        else if (suppliedToken.Length == 8 && suppliedToken.All(char.IsDigit))
        {
            tokenSecret = suppliedToken;
            var tokenHash = Hash(tokenSecret);
            token = await context.KioskCheckInTokens
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.ClinicId == station.ClinicId && item.TokenHash == tokenHash, cancellationToken);
        }
        else
        {
            return SettingsOperationResult<KioskCheckInResult>.NotFound();
        }

        if (token is null || token.ConsumedAtUtc is not null || token.ExpiresAtUtc <= DateTime.UtcNow ||
            !VerifyTokenSecret(token.TokenHash, tokenSecret))
            return SettingsOperationResult<KioskCheckInResult>.NotFound();

        var checkIn = await appointmentCheckInWorkflow.CheckInAsync(token.AppointmentId, station.ClinicId, cancellationToken);
        if (checkIn.Status == AppointmentCheckInStatus.NotFound) return SettingsOperationResult<KioskCheckInResult>.NotFound();
        if (checkIn.Status is AppointmentCheckInStatus.Ineligible or AppointmentCheckInStatus.PaymentRequired)
            return SettingsOperationResult<KioskCheckInResult>.Validation(
                new Dictionary<string, string[]>
                {
                    ["appointmentToken"] = [checkIn.Status == AppointmentCheckInStatus.PaymentRequired
                        ? "Please complete required payment with clinic staff before check-in."
                        : "This appointment cannot be checked in."]
                });

        var checkedInAt = checkIn.CheckedInAtUtc ?? DateTime.UtcNow;
        token.ConsumedAtUtc = checkedInAt;
        station.LastSeenAtUtc = checkedInAt;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = "KioskAppointmentCheckedIn",
            EntityType = nameof(Appointment),
            EntityId = token.AppointmentId,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = station.ClinicId,
                ["stationId"] = station.Id,
                ["appointmentId"] = token.AppointmentId
            }
        }, cancellationToken);
        return SettingsOperationResult<KioskCheckInResult>.Success(new KioskCheckInResult(token.AppointmentId, checkedInAt));
    }

    private static (KioskEnrollmentCode Entity, string PlainText) CreateEnrollmentCode(KioskStation station)
    {
        var secret = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
        var entity = new KioskEnrollmentCode
        {
            ClinicId = station.ClinicId,
            KioskStationId = station.Id,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(secret, 12),
            ExpiresAtUtc = DateTime.UtcNow.Add(EnrollmentLifetime)
        };
        return (entity, $"{entity.Id:N}.{secret}");
    }

    private async Task<string> CreateUniqueNumericCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
            var hash = Hash(code);
            if (!await context.KioskCheckInTokens.IgnoreQueryFilters()
                    .AnyAsync(item => item.TokenHash == hash, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("A unique kiosk check-in code could not be generated.");
    }

    private Task AuditAsync(
        string eventType,
        Guid clinicId,
        string entityType,
        Guid entityId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["clinicId"] = clinicId,
            ["entityId"] = entityId
        };
        if (additionalMetadata is not null)
        {
            foreach (var item in additionalMetadata)
            {
                metadata[item.Key] = item.Value;
            }
        }

        return auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = eventType,
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata
        }, cancellationToken);
    }

    private static KioskStationDto MapStation(KioskStation station) =>
        new(station.Id, station.Name, station.IsActive, station.LastSeenAtUtc, station.Version);

    private static string BuildSecretToken(Guid id, int bytes)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
        return $"{id:N}.{secret}";
    }

    private static bool TrySplitToken(string value, out Guid id, out string secret)
    {
        id = Guid.Empty;
        secret = string.Empty;
        var separator = value.IndexOf('.');
        if (separator <= 0 || separator == value.Length - 1) return false;
        if (!Guid.TryParseExact(value[..separator], "N", out id)) return false;
        secret = value[(separator + 1)..];
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool VerifyTokenSecret(string storedHash, string secret) =>
        storedHash.StartsWith("$2", StringComparison.Ordinal)
            ? BCrypt.Net.BCrypt.Verify(secret, storedHash)
            : FixedTimeEquals(storedHash, Hash(secret));

    private static bool FixedTimeEquals(string left, string right) =>
        left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
