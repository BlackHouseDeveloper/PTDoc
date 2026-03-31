using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Identity;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PTDoc.Infrastructure.Data.Seeders;

/// <summary>
/// Seeds the database with initial test data for development.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Well-known ID for the default development clinic (Sprint J).
    /// Matches the demo clinic_id claim in CredentialValidator.
    /// </summary>
    public static readonly Guid DefaultClinicId = Guid.Parse("00000000-0000-0000-0000-000000000100");

    private const int TargetActivePtCount = 3;
    private const int TargetActivePtaCount = 7;
    private const int TargetPatientCount = 60;
    private const int SeedScheduleStartHour = 7;
    private const int SeedScheduleEndHour = 18;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private static readonly IReadOnlyList<StaffSeedSpec> PreferredPtStaff =
    [
        new("testuser", "Test", "User", "test@ptdoc.local", Roles.Admin, "PT123456", "CA"),
        new("amorgan", "Alex", "Morgan", "alex.morgan@ptdoc.local", Roles.PT, "PT123457", "CA"),
        new("nshah", "Nina", "Shah", "nina.shah@ptdoc.local", Roles.PT, "PT123458", "CA"),
        new("dpark", "Daniel", "Park", "daniel.park@ptdoc.local", Roles.PT, "PT123459", "CA"),
        new("ehughes", "Erin", "Hughes", "erin.hughes@ptdoc.local", Roles.PT, "PT123460", "CA")
    ];

    private static readonly IReadOnlyList<StaffSeedSpec> PreferredPtaStaff =
    [
        new("rlopez", "Rosa", "Lopez", "rosa.lopez@ptdoc.local", Roles.PTA, "PTA223451", "CA"),
        new("jkim", "Jordan", "Kim", "jordan.kim@ptdoc.local", Roles.PTA, "PTA223452", "CA"),
        new("mgarcia", "Maya", "Garcia", "maya.garcia@ptdoc.local", Roles.PTA, "PTA223453", "CA"),
        new("tnguyen", "Theo", "Nguyen", "theo.nguyen@ptdoc.local", Roles.PTA, "PTA223454", "CA"),
        new("sbrooks", "Sofia", "Brooks", "sofia.brooks@ptdoc.local", Roles.PTA, "PTA223455", "CA"),
        new("lpatel", "Lena", "Patel", "lena.patel@ptdoc.local", Roles.PTA, "PTA223456", "CA"),
        new("cprice", "Cameron", "Price", "cameron.price@ptdoc.local", Roles.PTA, "PTA223457", "CA"),
        new("wross", "Wesley", "Ross", "wesley.ross@ptdoc.local", Roles.PTA, "PTA223458", "CA"),
        new("ahall", "Avery", "Hall", "avery.hall@ptdoc.local", Roles.PTA, "PTA223459", "CA")
    ];

    private static readonly PatientSeedCondition[] PatientConditions =
    [
        new("M54.2", "Cervicalgia", BodyPart.Cervical, OutcomeMeasureType.NeckDisabilityIndex, "Limited cervical rotation", "Right", "degrees", "45", 24, "difficulty turning the head while driving", "cervical mobility and postural control"),
        new("M54.50", "Low back pain, unspecified", BodyPart.Lumbar, OutcomeMeasureType.OswestryDisabilityIndex, "Lumbar flexion tolerance", "Bilateral", "degrees", "60", 32, "difficulty sitting longer than 20 minutes", "lumbar stabilization and lifting mechanics"),
        new("M25.511", "Pain in right shoulder", BodyPart.Shoulder, OutcomeMeasureType.DASH, "Shoulder flexion", "Right", "degrees", "130", 41, "difficulty reaching overhead cabinets", "rotator cuff strengthening and scapular control"),
        new("M25.561", "Pain in right knee", BodyPart.Knee, OutcomeMeasureType.LEFS, "Knee flexion", "Right", "degrees", "110", 46, "difficulty negotiating stairs", "quad strengthening and gait retraining"),
        new("M25.551", "Pain in right hip", BodyPart.Hip, OutcomeMeasureType.LEFS, "Hip flexion", "Right", "degrees", "92", 38, "difficulty with sit-to-stand transfers", "hip strength and balance progression")
    ];

    private static readonly string[] PatientFirstNames =
    [
        "Ava", "Mia", "Liam", "Noah", "Emma", "Olivia", "Ethan", "Sophia", "Lucas", "Amelia",
        "Harper", "Mason", "Elijah", "Ella", "James", "Isabella", "Benjamin", "Charlotte", "Henry", "Grace"
    ];

    private static readonly string[] PatientLastNames =
    [
        "Adams", "Baker", "Campbell", "Diaz", "Edwards", "Foster", "Gutierrez", "Howard", "Irwin", "Jackson",
        "Keller", "Lawson", "Mitchell", "Owens", "Perry", "Quinn", "Ramirez", "Sullivan", "Turner", "Watson"
    ];

    /// <summary>
    /// Seeds development data idempotently. Existing records are preserved and only missing
    /// clinic, staffing, patient, appointment, and note coverage is added.
    /// </summary>
    public static async Task SeedTestDataAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Checking development seed data...");

        var now = DateTime.UtcNow;
        var clinic = await EnsureDefaultClinicAsync(context, logger, now);

        await EnsureSystemUserAsync(context, logger, now);
        await EnsurePreferredStaffAsync(context, clinic.Id, Roles.PT, TargetActivePtCount, PreferredPtStaff, logger, now);
        await EnsurePreferredStaffAsync(context, clinic.Id, Roles.PTA, TargetActivePtaCount, PreferredPtaStaff, logger, now);
        await EnsurePendingUserAsync(context, clinic.Id, logger, now);

        var ptClinicians = await context.Users
            .Where(u => u.ClinicId == clinic.Id && u.IsActive && u.Role == Roles.PT)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        var ptaClinicians = await context.Users
            .Where(u => u.ClinicId == clinic.Id && u.IsActive && u.Role == Roles.PTA)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        var seedActorId = ptClinicians.FirstOrDefault()?.Id ?? IIdentityContextAccessor.SystemUserId;

        await EnsurePatientsAsync(context, clinic.Id, seedActorId, logger, now);
        await EnsureAppointmentsAsync(context, clinic.Id, seedActorId, ptClinicians, ptaClinicians, logger, now);
        await EnsureTodayShowcaseAppointmentsAsync(context, clinic.Id, seedActorId, ptClinicians, ptaClinicians, logger, now);
        await NormalizeSeedAppointmentsAsync(context, clinic.Id, seedActorId, logger, now);
        await EnsureNotesAsync(context, clinic.Id, ptClinicians, ptaClinicians, logger, now);

        var activePtCount = await context.Users.CountAsync(u => u.ClinicId == clinic.Id && u.IsActive && u.Role == Roles.PT);
        var activePtaCount = await context.Users.CountAsync(u => u.ClinicId == clinic.Id && u.IsActive && u.Role == Roles.PTA);
        var pendingCount = await context.Users.CountAsync(u => u.ClinicId == clinic.Id && !u.IsActive && u.Role != "System");
        var patientCount = await context.Patients.CountAsync(p => p.ClinicId == clinic.Id && !p.IsArchived);
        var appointmentCount = await context.Appointments.CountAsync(a => a.ClinicId == clinic.Id);
        var noteCount = await context.ClinicalNotes.CountAsync(n => n.ClinicId == clinic.Id);

        logger.LogInformation(
            "Development seed complete for clinic {ClinicId}. PT={PtCount}, PTA={PtaCount}, Pending={PendingCount}, Patients={PatientCount}, Appointments={AppointmentCount}, Notes={NoteCount}.",
            clinic.Id,
            activePtCount,
            activePtaCount,
            pendingCount,
            patientCount,
            appointmentCount,
            noteCount);
    }

    private static async Task<Clinic> EnsureDefaultClinicAsync(
        ApplicationDbContext context,
        ILogger logger,
        DateTime now)
    {
        var clinic = await context.Clinics.FirstOrDefaultAsync(c => c.Id == DefaultClinicId);
        if (clinic is not null)
        {
            return clinic;
        }

        clinic = new Clinic
        {
            Id = DefaultClinicId,
            Name = "PTDoc Development Clinic",
            Slug = "ptdoc-dev",
            IsActive = true,
            CreatedAt = now
        };

        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        logger.LogInformation("Seeded default development clinic.");
        return clinic;
    }

    private static async Task EnsureSystemUserAsync(
        ApplicationDbContext context,
        ILogger logger,
        DateTime now)
    {
        var systemUser = await context.Users.FirstOrDefaultAsync(u => u.Id == IIdentityContextAccessor.SystemUserId);
        if (systemUser is not null)
        {
            return;
        }

        context.Users.Add(new User
        {
            Id = IIdentityContextAccessor.SystemUserId,
            Username = "system",
            PinHash = AuthService.HashPin("system-not-for-login"),
            FirstName = "System",
            LastName = "User",
            Role = "System",
            IsActive = false,
            CreatedAt = now
        });

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded system user.");
    }

    private static async Task EnsurePreferredStaffAsync(
        ApplicationDbContext context,
        Guid clinicId,
        string role,
        int targetCount,
        IReadOnlyList<StaffSeedSpec> preferredSpecs,
        ILogger logger,
        DateTime now)
    {
        var activeCount = await context.Users.CountAsync(u => u.ClinicId == clinicId && u.IsActive && u.Role == role);
        if (activeCount >= targetCount)
        {
            return;
        }

        foreach (var spec in preferredSpecs)
        {
            if (activeCount >= targetCount)
            {
                break;
            }

            var existingUser = await context.Users.FirstOrDefaultAsync(u => u.Username == spec.Username);
            if (existingUser is not null)
            {
                if (existingUser.ClinicId == null)
                {
                    existingUser.ClinicId = clinicId;
                }

                await context.SaveChangesAsync();
                activeCount = await context.Users.CountAsync(u => u.ClinicId == clinicId && u.IsActive && u.Role == role);
                continue;
            }

            context.Users.Add(CreateStaffUser(spec, clinicId, now));
            activeCount++;
        }

        var fallbackIndex = 1;
        while (activeCount < targetCount)
        {
            var username = $"dev-{role.ToLowerInvariant()}-{fallbackIndex:00}";
            var email = $"{username}@ptdoc.local";
            fallbackIndex++;

            if (await context.Users.AnyAsync(u => u.Username == username || (u.Email != null && u.Email == email)))
            {
                continue;
            }

            context.Users.Add(CreateStaffUser(
                new StaffSeedSpec(
                    username,
                    role == Roles.PT ? "Dev" : "Seed",
                    role == Roles.PT ? $"Therapist{fallbackIndex:00}" : $"Assistant{fallbackIndex:00}",
                    email,
                    role,
                    role == Roles.PT ? $"PT9{fallbackIndex:05}" : $"PTA9{fallbackIndex:05}",
                    "CA"),
                clinicId,
                now));
            activeCount++;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Ensured {Role} clinician coverage. Active count: {Count}.", role, activeCount);
    }

    private static User CreateStaffUser(StaffSeedSpec spec, Guid clinicId, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Username = spec.Username,
            PinHash = AuthService.HashPin("1234"),
            FirstName = spec.FirstName,
            LastName = spec.LastName,
            Role = spec.Role,
            Email = spec.Email,
            IsActive = true,
            CreatedAt = now,
            LicenseNumber = spec.LicenseNumber,
            LicenseState = spec.LicenseState,
            LicenseExpirationDate = now.AddYears(2),
            ClinicId = clinicId
        };

    private static async Task EnsurePendingUserAsync(
        ApplicationDbContext context,
        Guid clinicId,
        ILogger logger,
        DateTime now)
    {
        var existingPending = await context.Users.AnyAsync(u => u.ClinicId == clinicId && !u.IsActive && u.Role != "System");
        if (existingPending)
        {
            return;
        }

        const string username = "pending.pta";
        var uniqueUsername = username;
        var suffix = 2;
        while (await context.Users.AnyAsync(u => u.Username == uniqueUsername))
        {
            uniqueUsername = $"pending.pta{suffix++}";
        }

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = uniqueUsername,
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Pending",
            LastName = "Assistant",
            Role = Roles.PTA,
            Email = $"{uniqueUsername}@ptdoc.local",
            IsActive = false,
            CreatedAt = now.AddMinutes(-15),
            LicenseNumber = "PTA998877",
            LicenseState = "CA",
            LicenseExpirationDate = now.AddYears(1),
            ClinicId = clinicId
        });

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded a pending clinic user for approval workflow coverage.");
    }

    private static async Task EnsurePatientsAsync(
        ApplicationDbContext context,
        Guid clinicId,
        Guid seedActorId,
        ILogger logger,
        DateTime now)
    {
        var existingPatients = await context.Patients
            .Where(p => p.ClinicId == clinicId && !p.IsArchived)
            .ToListAsync();

        if (existingPatients.Count >= TargetPatientCount)
        {
            return;
        }

        var existingMrns = existingPatients
            .Where(p => !string.IsNullOrWhiteSpace(p.MedicalRecordNumber))
            .Select(p => p.MedicalRecordNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var needed = TargetPatientCount - existingPatients.Count;
        var created = 0;
        var candidateIndex = 1;

        while (created < needed)
        {
            var medicalRecordNumber = $"DEV-PT-{candidateIndex:000}";
            candidateIndex++;

            if (!existingMrns.Add(medicalRecordNumber))
            {
                continue;
            }

            var patient = BuildPatientSeed(candidateIndex, clinicId, seedActorId, now, medicalRecordNumber);
            context.Patients.Add(patient);
            created++;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} patients to reach development target volume.", created);
    }

    private static Patient BuildPatientSeed(
        int candidateIndex,
        Guid clinicId,
        Guid seedActorId,
        DateTime now,
        string medicalRecordNumber)
    {
        var stableIndex = candidateIndex - 1;
        var firstName = PatientFirstNames[stableIndex % PatientFirstNames.Length];
        var lastName = PatientLastNames[(stableIndex / PatientFirstNames.Length) % PatientLastNames.Length];
        var condition = PatientConditions[stableIndex % PatientConditions.Length];
        var payerType = stableIndex % 5 == 0 ? "Medicare" : "Commercial";
        var dateOfBirth = new DateTime(1958 + (stableIndex % 35), ((stableIndex % 12) + 1), ((stableIndex % 27) + 1));
        var consentSigned = stableIndex % 6 != 0;

        return new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            Email = $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}{candidateIndex:000}@ptdoc.local",
            Phone = $"555-01{candidateIndex % 10}{(candidateIndex / 10) % 10}{candidateIndex % 10}",
            AddressLine1 = $"{100 + candidateIndex} Rehab Lane",
            City = "San Diego",
            State = "CA",
            ZipCode = $"{92100 + (candidateIndex % 50)}",
            MedicalRecordNumber = medicalRecordNumber,
            ReferringPhysician = $"Dr. {PatientLastNames[(stableIndex + 3) % PatientLastNames.Length]}",
            PhysicianNpi = $"{1400000000 + stableIndex:0000000000}",
            DateOfOnset = now.Date.AddDays(-(14 + (stableIndex % 90))),
            AuthorizationNumber = stableIndex % 4 == 0 ? $"AUTH-{candidateIndex:0000}" : null,
            EmergencyContactName = $"{PatientFirstNames[(stableIndex + 4) % PatientFirstNames.Length]} {PatientLastNames[(stableIndex + 7) % PatientLastNames.Length]}",
            EmergencyContactPhone = $"555-02{candidateIndex % 10}{(candidateIndex / 10) % 10}{candidateIndex % 10}",
            ConsentSigned = consentSigned,
            ConsentSignedDate = consentSigned ? now.Date.AddDays(-(stableIndex % 30)) : null,
            DiagnosisCodesJson = SerializeJson(new[]
            {
                new
                {
                    IcdCode = condition.DiagnosisCode,
                    Description = condition.DiagnosisDescription,
                    IsPrimary = true
                }
            }),
            PayerInfoJson = SerializeJson(new
            {
                PayerType = payerType,
                PlanName = payerType == "Medicare" ? "Medicare Part B" : "Blue Pacific PPO",
                MemberId = $"MEM{candidateIndex:000000}",
                Copay = payerType == "Medicare" ? 0 : 30
            }),
            ClinicId = clinicId,
            LastModifiedUtc = now,
            ModifiedByUserId = seedActorId,
            SyncState = SyncState.Pending
        };
    }

    private static async Task EnsureAppointmentsAsync(
        ApplicationDbContext context,
        Guid clinicId,
        Guid seedActorId,
        IReadOnlyList<User> ptClinicians,
        IReadOnlyList<User> ptaClinicians,
        ILogger logger,
        DateTime now)
    {
        if (ptClinicians.Count == 0)
        {
            logger.LogWarning("Skipping appointment seeding because no active PT clinicians are available.");
            return;
        }

        var clinicianRoles = await context.Users
            .Where(u => u.ClinicId == clinicId)
            .ToDictionaryAsync(u => u.Id, u => u.Role);

        var patients = await context.Patients
            .Where(p => p.ClinicId == clinicId && !p.IsArchived)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();

        var existingAppointments = await context.Appointments
            .Where(a => a.ClinicId == clinicId)
            .ToListAsync();

        var schedulableCounts = existingAppointments
            .Where(a => clinicianRoles.TryGetValue(a.ClinicalId, out var role) && (role == Roles.PT || role == Roles.PTA))
            .GroupBy(a => a.PatientId)
            .ToDictionary(g => g.Key, g => g.Count());

        var created = 0;
        foreach (var patient in patients)
        {
            var key = GetStableSeedKey(patient.Id);
            var targetSlots = 1 + (key % 2 == 0 ? 1 : 0) + (key % 3 == 0 ? 1 : 0);
            var existingCount = schedulableCounts.TryGetValue(patient.Id, out var count) ? count : 0;

            for (var slot = 1; slot <= targetSlots && existingCount < targetSlots; slot++)
            {
                var marker = BuildAppointmentMarker(patient.Id, slot);
                if (existingAppointments.Any(a => a.PatientId == patient.Id && string.Equals(a.Notes, marker, StringComparison.Ordinal)))
                {
                    continue;
                }

                var appointment = BuildSeedAppointment(patient, slot, marker, ptClinicians, ptaClinicians, seedActorId, now);
                MoveAppointmentToNextAvailableSlot(appointment, existingAppointments);
                context.Appointments.Add(appointment);
                existingAppointments.Add(appointment);
                existingCount++;
                created++;
            }

            schedulableCounts[patient.Id] = existingCount;
        }

        if (created == 0)
        {
            return;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} appointments to satisfy clinician workflow coverage.", created);
    }

    private static async Task EnsureTodayShowcaseAppointmentsAsync(
        ApplicationDbContext context,
        Guid clinicId,
        Guid seedActorId,
        IReadOnlyList<User> ptClinicians,
        IReadOnlyList<User> ptaClinicians,
        ILogger logger,
        DateTime now)
    {
        var clinicians = ptClinicians.Concat(ptaClinicians).ToList();
        if (clinicians.Count == 0)
        {
            return;
        }

        var patients = await context.Patients
            .Where(p => p.ClinicId == clinicId && !p.IsArchived)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();

        if (patients.Count == 0)
        {
            return;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.Local);
        var localToday = localNow.Date;

        var existingAppointments = await context.Appointments
            .Where(a => a.ClinicId == clinicId)
            .ToListAsync();

        var created = 0;
        for (var index = 0; index < clinicians.Count; index++)
        {
            var clinician = clinicians[index];
            var marker = BuildTodayShowcaseMarker(localToday, clinician.Id);

            if (existingAppointments.Any(a => string.Equals(a.Notes, marker, StringComparison.Ordinal)))
            {
                continue;
            }

            var hasVisibleBusinessHoursAppointmentToday = existingAppointments.Any(a =>
                a.ClinicalId == clinician.Id &&
                IsSameLocalDate(a.StartTimeUtc, localToday) &&
                GetLocalHour(a.StartTimeUtc) >= 7 &&
                GetLocalHour(a.StartTimeUtc) < 18);

            if (hasVisibleBusinessHoursAppointmentToday)
            {
                continue;
            }

            var patient = patients[index % patients.Count];
            var localStart = localToday
                .AddHours(9 + (index % 6))
                .AddMinutes((index % 2) * 30);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local);

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                PatientId = patient.Id,
                ClinicalId = clinician.Id,
                StartTimeUtc = startUtc,
                EndTimeUtc = startUtc.AddMinutes(45),
                AppointmentType = AppointmentType.FollowUp,
                Status = localStart <= localNow ? AppointmentStatus.CheckedIn : AppointmentStatus.Confirmed,
                Notes = marker,
                ClinicId = patient.ClinicId,
                LastModifiedUtc = now,
                ModifiedByUserId = seedActorId,
                SyncState = SyncState.Pending
            };

            MoveAppointmentToNextAvailableSlot(appointment, existingAppointments);
            context.Appointments.Add(appointment);
            existingAppointments.Add(appointment);
            created++;
        }

        if (created == 0)
        {
            return;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} daytime showcase appointments for today's scheduler view.", created);
    }

    private static async Task NormalizeSeedAppointmentsAsync(
        ApplicationDbContext context,
        Guid clinicId,
        Guid seedActorId,
        ILogger logger,
        DateTime now)
    {
        var appointments = await context.Appointments
            .Where(a => a.ClinicId == clinicId)
            .OrderBy(a => a.ClinicalId)
            .ThenBy(a => a.StartTimeUtc)
            .ThenBy(a => a.Id)
            .ToListAsync();

        if (appointments.Count == 0)
        {
            return;
        }

        var adjustedAppointments = new Dictionary<Guid, Appointment>();

        foreach (var clinicianGroup in appointments.GroupBy(a => a.ClinicalId))
        {
            var occupiedSlots = clinicianGroup
                .OrderBy(a => a.StartTimeUtc)
                .ToList();

            foreach (var appointment in clinicianGroup
                         .Where(IsSeedAppointment)
                         .OrderBy(a => a.StartTimeUtc)
                         .ThenBy(a => a.Id))
            {
                var originalStart = appointment.StartTimeUtc;
                var originalEnd = appointment.EndTimeUtc;

                MoveAppointmentToNextAvailableSlot(appointment, occupiedSlots);
                occupiedSlots.Add(appointment);

                if (appointment.StartTimeUtc == originalStart && appointment.EndTimeUtc == originalEnd)
                {
                    continue;
                }

                appointment.LastModifiedUtc = now;
                appointment.ModifiedByUserId = seedActorId;
                adjustedAppointments[appointment.Id] = appointment;
                await context.SaveChangesAsync();
            }
        }

        if (adjustedAppointments.Count == 0)
        {
            return;
        }

        var adjustedAppointmentIds = adjustedAppointments.Keys.ToList();
        var notes = await context.ClinicalNotes
            .Where(note => note.ClinicId == clinicId
                && note.AppointmentId != null
                && adjustedAppointmentIds.Contains(note.AppointmentId.Value))
            .ToListAsync();

        foreach (var note in notes)
        {
            var appointment = adjustedAppointments[note.AppointmentId!.Value];
            note.DateOfService = appointment.StartTimeUtc;
            note.LastModifiedUtc = now;
            note.ModifiedByUserId = seedActorId;
        }

        var adjustedNoteIds = notes.Select(note => note.Id).ToList();
        if (adjustedNoteIds.Count > 0)
        {
            var notesById = notes.ToDictionary(note => note.Id);
            var outcomeMeasures = await context.OutcomeMeasureResults
                .Where(result => result.NoteId != null && adjustedNoteIds.Contains(result.NoteId.Value))
                .ToListAsync();

            foreach (var outcomeMeasure in outcomeMeasures)
            {
                if (outcomeMeasure.NoteId is null)
                {
                    continue;
                }

                outcomeMeasure.DateRecorded = notesById[outcomeMeasure.NoteId.Value].DateOfService;
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Adjusted {Count} seeded appointments to remove clinician overlaps and keep seed hours within the daytime window.", adjustedAppointments.Count);
    }

    private static Appointment BuildSeedAppointment(
        Patient patient,
        int slot,
        string marker,
        IReadOnlyList<User> ptClinicians,
        IReadOnlyList<User> ptaClinicians,
        Guid seedActorId,
        DateTime now)
    {
        var key = GetStableSeedKey(patient.Id);
        var clinicianPool = ptaClinicians.Count > 0 ? ptaClinicians : ptClinicians;
        var combinedClinicians = ptClinicians.Concat(clinicianPool).ToList();

        DateTime startTimeUtc;
        AppointmentType appointmentType;
        AppointmentStatus status;
        User clinician;
        var durationMinutes = 45;

        switch (slot)
        {
            case 1:
                clinician = ptClinicians[key % ptClinicians.Count];
                appointmentType = AppointmentType.InitialEvaluation;
                status = AppointmentStatus.Completed;
                durationMinutes = 60;
                startTimeUtc = now.Date.AddDays(-(21 + (key % 28))).AddHours(8 + (key % 5));
                break;
            case 2:
                clinician = clinicianPool[key % clinicianPool.Count];
                appointmentType = AppointmentType.FollowUp;
                durationMinutes = 45;
                if (key % 4 == 0)
                {
                    var localNow = ToLocalDateTime(now);
                    var localCandidateStart = localNow.AddMinutes(-(15 + (key % 45)));
                    localCandidateStart = new DateTime(
                        localCandidateStart.Year,
                        localCandidateStart.Month,
                        localCandidateStart.Day,
                        localCandidateStart.Hour,
                        localCandidateStart.Minute - (localCandidateStart.Minute % 15),
                        0,
                        localCandidateStart.Kind);
                    var localStart = ClampSeedStartToBusinessWindow(localCandidateStart, TimeSpan.FromMinutes(durationMinutes));
                    var localEnd = localStart.AddMinutes(durationMinutes);

                    status = localNow switch
                    {
                        _ when localNow < localStart => AppointmentStatus.Confirmed,
                        _ when localNow >= localEnd => AppointmentStatus.Completed,
                        _ => AppointmentStatus.InProgress
                    };
                    startTimeUtc = ToUtcDateTime(localStart);
                }
                else
                {
                    status = AppointmentStatus.Completed;
                    startTimeUtc = now.Date.AddDays(-(4 + (key % 10))).AddHours(9 + (key % 6));
                }

                break;
            default:
                clinician = combinedClinicians[key % combinedClinicians.Count];
                appointmentType = AppointmentType.FollowUp;
                status = key % 2 == 0 ? AppointmentStatus.Scheduled : AppointmentStatus.Confirmed;
                durationMinutes = 45;
                startTimeUtc = now.Date.AddDays(3 + (key % 21)).AddHours(8 + (key % 7));
                break;
        }

        return new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ClinicalId = clinician.Id,
            StartTimeUtc = DateTime.SpecifyKind(startTimeUtc, DateTimeKind.Utc),
            EndTimeUtc = DateTime.SpecifyKind(startTimeUtc.AddMinutes(durationMinutes), DateTimeKind.Utc),
            AppointmentType = appointmentType,
            Status = status,
            Notes = marker,
            ClinicId = patient.ClinicId,
            LastModifiedUtc = now,
            ModifiedByUserId = seedActorId,
            SyncState = SyncState.Pending
        };
    }

    private static async Task EnsureNotesAsync(
        ApplicationDbContext context,
        Guid clinicId,
        IReadOnlyList<User> ptClinicians,
        IReadOnlyList<User> ptaClinicians,
        ILogger logger,
        DateTime now)
    {
        var appointments = await context.Appointments
            .Where(a => a.ClinicId == clinicId)
            .OrderBy(a => a.StartTimeUtc)
            .ToListAsync();

        if (appointments.Count == 0)
        {
            return;
        }

        var patients = await context.Patients
            .Where(p => p.ClinicId == clinicId)
            .ToDictionaryAsync(p => p.Id);

        var clinicians = await context.Users
            .Where(u => u.ClinicId == clinicId && u.IsActive)
            .ToDictionaryAsync(u => u.Id);

        var existingNoteAppointmentIds = (await context.ClinicalNotes
            .Where(n => n.ClinicId == clinicId && n.AppointmentId != null)
            .Select(n => n.AppointmentId!.Value)
            .ToListAsync())
            .ToHashSet();

        var createdNotes = 0;
        foreach (var appointment in appointments)
        {
            if (existingNoteAppointmentIds.Contains(appointment.Id))
            {
                continue;
            }

            if (!patients.TryGetValue(appointment.PatientId, out var patient))
            {
                continue;
            }

            if (!clinicians.TryGetValue(appointment.ClinicalId, out var clinician))
            {
                continue;
            }

            if (clinician.Role != Roles.PT && clinician.Role != Roles.PTA)
            {
                continue;
            }

            var noteBundle = BuildSeedNoteBundle(appointment, patient, clinician, ptClinicians, ptaClinicians, now);
            context.ClinicalNotes.Add(noteBundle.Note);

            if (noteBundle.ObjectiveMetric is not null)
            {
                context.ObjectiveMetrics.Add(noteBundle.ObjectiveMetric);
            }

            if (noteBundle.OutcomeMeasure is not null)
            {
                context.OutcomeMeasureResults.Add(noteBundle.OutcomeMeasure);
            }

            existingNoteAppointmentIds.Add(appointment.Id);
            createdNotes++;
        }

        if (createdNotes == 0)
        {
            return;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} clinical notes across appointment states.", createdNotes);
    }

    private static SeedNoteBundle BuildSeedNoteBundle(
        Appointment appointment,
        Patient patient,
        User clinician,
        IReadOnlyList<User> ptClinicians,
        IReadOnlyList<User> ptaClinicians,
        DateTime now)
    {
        var key = GetStableSeedKey(appointment.Id);
        var condition = PatientConditions[GetStableSeedKey(patient.Id) % PatientConditions.Length];
        var noteType = appointment.AppointmentType switch
        {
            AppointmentType.InitialEvaluation => NoteType.Evaluation,
            AppointmentType.Discharge => NoteType.Discharge,
            _ => NoteType.Daily
        };

        var shouldBeSigned = appointment.StartTimeUtc < now.AddDays(-1) && HasDiagnosisCodes(patient);
        if (noteType == NoteType.Evaluation && clinician.Role != Roles.PT)
        {
            shouldBeSigned = false;
        }

        var contentJson = noteType switch
        {
            NoteType.Evaluation when shouldBeSigned => BuildSignedEvaluationContent(patient, condition, appointment),
            NoteType.Evaluation => BuildDraftEvaluationContent(patient, condition, appointment),
            NoteType.Discharge when shouldBeSigned => BuildSignedDischargeContent(patient, condition),
            NoteType.Discharge => BuildDraftDischargeContent(patient, condition),
            _ when shouldBeSigned => BuildSignedDailyContent(patient, condition, appointment),
            _ => BuildDraftDailyContent(patient, condition, appointment)
        };

        var cptCodesJson = noteType == NoteType.Evaluation
            ? SerializeJson(new[]
            {
                new { Code = "97161", Description = "PT Evaluation, low complexity", Units = 1 }
            })
            : SerializeJson(new[]
            {
                new { Code = "97110", Description = "Therapeutic exercise", Units = 2 },
                new { Code = "97112", Description = "Neuromuscular re-education", Units = 1 }
            });

        var lastModifiedUtc = shouldBeSigned ? appointment.EndTimeUtc : now;
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            AppointmentId = appointment.Id,
            NoteType = noteType,
            IsReEvaluation = false,
            NoteStatus = NoteStatus.Draft,
            TherapistNpi = BuildTherapistNpi(clinician.Id),
            TotalTreatmentMinutes = noteType == NoteType.Evaluation ? 60 : 38,
            ContentJson = contentJson,
            DateOfService = appointment.StartTimeUtc,
            CptCodesJson = cptCodesJson,
            ClinicId = patient.ClinicId,
            LastModifiedUtc = lastModifiedUtc,
            ModifiedByUserId = clinician.Id,
            SyncState = SyncState.Pending
        };

        ObjectiveMetric? objectiveMetric = null;
        OutcomeMeasureResult? outcomeMeasure = null;

        if (noteType == NoteType.Evaluation)
        {
            objectiveMetric = new ObjectiveMetric
            {
                Id = Guid.NewGuid(),
                NoteId = note.Id,
                BodyPart = condition.BodyPart,
                MetricType = MetricType.ROM,
                Value = condition.ObjectiveValue,
                Side = condition.Side,
                Unit = condition.Unit,
                IsWNL = false,
                LastModifiedUtc = lastModifiedUtc
            };

            outcomeMeasure = new OutcomeMeasureResult
            {
                Id = Guid.NewGuid(),
                PatientId = patient.Id,
                MeasureType = condition.OutcomeMeasure,
                Score = condition.BaselineScore,
                DateRecorded = appointment.StartTimeUtc,
                ClinicianId = clinician.Id,
                NoteId = note.Id,
                ClinicId = patient.ClinicId
            };
        }

        if (!shouldBeSigned)
        {
            return new SeedNoteBundle(note, objectiveMetric, outcomeMeasure);
        }

        var signedAt = appointment.EndTimeUtc == default ? lastModifiedUtc : appointment.EndTimeUtc;
        note.SignatureHash = ComputeSignatureHash(note.PatientId, note.DateOfService, note.NoteType, note.ContentJson, note.CptCodesJson);
        note.SignedUtc = signedAt;
        note.SignedByUserId = clinician.Id;

        if (clinician.Role == Roles.PTA && noteType == NoteType.Daily)
        {
            note.RequiresCoSign = true;
            if (key % 2 == 0)
            {
                note.NoteStatus = NoteStatus.PendingCoSign;
            }
            else
            {
                var cosigner = ptClinicians[key % ptClinicians.Count];
                note.CoSignedByUserId = cosigner.Id;
                note.CoSignedUtc = signedAt.AddMinutes(20);
                note.NoteStatus = NoteStatus.Signed;
            }
        }
        else
        {
            note.NoteStatus = NoteStatus.Signed;
        }

        return new SeedNoteBundle(note, objectiveMetric, outcomeMeasure);
    }

    private static string BuildSignedEvaluationContent(Patient patient, PatientSeedCondition condition, Appointment appointment)
    {
        var certificationStart = appointment.StartTimeUtc.Date;
        var certificationEnd = certificationStart.AddDays(42);

        return SerializeJson(new
        {
            subjective = $"Patient reports {condition.DiagnosisDescription.ToLowerInvariant()} with {condition.FunctionalLimitation}. Symptoms have limited work and household activity tolerance.",
            objective = $"{condition.ObjectiveLabel} measured at {condition.ObjectiveValue} {condition.Unit} on the {condition.Side.ToLowerInvariant()} side.",
            assessment = $"Presentation is consistent with {condition.DiagnosisDescription.ToLowerInvariant()}. Prognosis is good with skilled PT and progressive loading.",
            goals = new[]
            {
                $"Reduce pain and improve {condition.ObjectiveLabel.ToLowerInvariant()} within 4 weeks.",
                $"Return to {condition.FunctionalLimitation.Replace("difficulty ", string.Empty)} without symptom flare."
            },
            plan = $"Skilled PT 2x/week for 6 weeks focused on {condition.PlanFocus}.",
            planOfCare = $"Therapeutic exercise, manual therapy, neuromuscular re-education, and home program progression for {condition.DiagnosisDescription.ToLowerInvariant()}.",
            certificationPeriod = $"{certificationStart:yyyy-MM-dd} to {certificationEnd:yyyy-MM-dd}",
            functionalLimitations = condition.FunctionalLimitation
        });
    }

    private static string BuildDraftEvaluationContent(Patient patient, PatientSeedCondition condition, Appointment appointment) =>
        SerializeJson(new
        {
            subjective = $"Initial evaluation started for {condition.DiagnosisDescription.ToLowerInvariant()}.",
            objective = string.Empty,
            assessment = "Draft evaluation in progress.",
            goals = Array.Empty<string>(),
            plan = string.Empty,
            planOfCare = string.Empty,
            certificationPeriod = string.Empty,
            functionalLimitations = condition.FunctionalLimitation
        });

    private static string BuildSignedDailyContent(Patient patient, PatientSeedCondition condition, Appointment appointment) =>
        SerializeJson(new
        {
            subjective = $"Patient reports improving tolerance with home exercise program but still notes {condition.FunctionalLimitation}.",
            objective = $"Completed therapeutic exercise and neuromuscular re-education; {condition.ObjectiveLabel.ToLowerInvariant()} remains limited at {condition.ObjectiveValue} {condition.Unit}.",
            assessment = "Patient tolerated session well and demonstrated improved movement quality with cueing.",
            plan = $"Continue skilled PT with emphasis on {condition.PlanFocus}. Progress exercises next visit as tolerated.",
            functionalLimitations = condition.FunctionalLimitation
        });

    private static string BuildDraftDailyContent(Patient patient, PatientSeedCondition condition, Appointment appointment) =>
        SerializeJson(new
        {
            subjective = $"Patient arrived for follow-up related to {condition.DiagnosisDescription.ToLowerInvariant()}.",
            objective = string.Empty,
            assessment = "Daily note draft not yet finalized.",
            plan = string.Empty
        });

    private static string BuildSignedDischargeContent(Patient patient, PatientSeedCondition condition) =>
        SerializeJson(new
        {
            subjective = $"Patient reports confidence managing {condition.DiagnosisDescription.ToLowerInvariant()} independently.",
            assessment = "Functional goals met and skilled PT no longer medically necessary.",
            plan = "Discharge to independent home exercise program.",
            functionalLimitations = $"Previously had {condition.FunctionalLimitation}; now independent with self-management."
        });

    private static string BuildDraftDischargeContent(Patient patient, PatientSeedCondition condition) =>
        SerializeJson(new
        {
            subjective = $"Discharge summary started for {condition.DiagnosisDescription.ToLowerInvariant()}.",
            assessment = "Pending final discharge review.",
            plan = string.Empty
        });

    private static string BuildAppointmentMarker(Guid patientId, int slot) =>
        $"[dev-seed] appointment:{patientId}:slot:{slot}";

    private static string BuildTodayShowcaseMarker(DateTime localDate, Guid clinicianId) =>
        $"[dev-seed] showcase:{localDate:yyyy-MM-dd}:{clinicianId:D}";

    private static bool IsSeedAppointment(Appointment appointment) =>
        appointment.Notes?.StartsWith("[dev-seed]", StringComparison.Ordinal) == true;

    private static void MoveAppointmentToNextAvailableSlot(
        Appointment appointment,
        IEnumerable<Appointment> existingAppointments)
    {
        var duration = appointment.EndTimeUtc - appointment.StartTimeUtc;
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(45);
        }

        var proposedStart = NormalizeSeedStartToBusinessWindow(appointment.StartTimeUtc, duration);

        while (true)
        {
            proposedStart = NormalizeSeedStartToBusinessWindow(proposedStart, duration);

            var conflictingAppointment = existingAppointments
                .Where(existing => existing.Id != appointment.Id
                    && existing.ClinicalId == appointment.ClinicalId
                    && existing.Status is not AppointmentStatus.Cancelled and not AppointmentStatus.NoShow
                    && existing.StartTimeUtc < proposedStart.Add(duration)
                    && proposedStart < existing.EndTimeUtc)
                .OrderBy(existing => existing.EndTimeUtc)
                .FirstOrDefault();

            if (conflictingAppointment is null)
            {
                break;
            }

            proposedStart = conflictingAppointment.EndTimeUtc;
        }

        appointment.StartTimeUtc = proposedStart;
        appointment.EndTimeUtc = proposedStart.Add(duration);
    }

    private static DateTime NormalizeSeedStartToBusinessWindow(DateTime proposedStartUtc, TimeSpan duration)
    {
        var localStart = ToLocalDateTime(proposedStartUtc);
        var clampedLocalStart = ClampSeedStartToBusinessWindow(localStart, duration);
        return ToUtcDateTime(clampedLocalStart);
    }

    private static DateTime ClampSeedStartToBusinessWindow(DateTime localStart, TimeSpan duration)
    {
        var earliestLocalStart = localStart.Date.AddHours(SeedScheduleStartHour);
        var latestLocalStart = localStart.Date.AddHours(SeedScheduleEndHour) - duration;

        if (latestLocalStart < earliestLocalStart)
        {
            latestLocalStart = earliestLocalStart;
        }

        if (localStart < earliestLocalStart)
        {
            return earliestLocalStart;
        }

        if (localStart > latestLocalStart)
        {
            return localStart.Date.AddDays(1).AddHours(SeedScheduleStartHour);
        }

        return localStart;
    }

    private static int GetStableSeedKey(Guid value)
    {
        var bytes = value.ToByteArray();
        return (int)(BitConverter.ToUInt32(bytes, 0) & 0x7FFFFFFF);
    }

    private static bool IsSameLocalDate(DateTime utcDateTime, DateTime localDate) =>
        ToLocalDateTime(utcDateTime).Date == localDate.Date;

    private static int GetLocalHour(DateTime utcDateTime) =>
        ToLocalDateTime(utcDateTime).Hour;

    private static DateTime ToLocalDateTime(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            utcDateTime.Kind == DateTimeKind.Utc ? utcDateTime : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            TimeZoneInfo.Local);

    private static DateTime ToUtcDateTime(DateTime localDateTime)
    {
        var normalizedLocalDateTime = localDateTime.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(localDateTime, TimeZoneInfo.Local),
            DateTimeKind.Local => localDateTime,
            _ => DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified)
        };

        return TimeZoneInfo.ConvertTimeToUtc(normalizedLocalDateTime, TimeZoneInfo.Local);
    }

    private static bool HasDiagnosisCodes(Patient patient)
    {
        if (string.IsNullOrWhiteSpace(patient.DiagnosisCodesJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(patient.DiagnosisCodesJson);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildTherapistNpi(Guid clinicianId)
    {
        var key = GetStableSeedKey(clinicianId);
        return $"{1000000000 + key % 900000000:0000000000}";
    }

    private static string ComputeSignatureHash(
        Guid patientId,
        DateTime dateOfService,
        NoteType noteType,
        string contentJson,
        string cptCodesJson)
    {
        var canonicalContent = JsonSerializer.Serialize(
            new
            {
                PatientId = patientId,
                DateOfService = dateOfService,
                NoteType = noteType,
                ContentJson = contentJson,
                CptCodesJson = cptCodesJson
            },
            JsonOptions);

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalContent));
        return Convert.ToHexString(hashBytes);
    }

    private static string SerializeJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed record StaffSeedSpec(
        string Username,
        string FirstName,
        string LastName,
        string Email,
        string Role,
        string LicenseNumber,
        string LicenseState);

    private sealed record PatientSeedCondition(
        string DiagnosisCode,
        string DiagnosisDescription,
        BodyPart BodyPart,
        OutcomeMeasureType OutcomeMeasure,
        string ObjectiveLabel,
        string Side,
        string Unit,
        string ObjectiveValue,
        double BaselineScore,
        string FunctionalLimitation,
        string PlanFocus);

    private sealed record SeedNoteBundle(
        ClinicalNote Note,
        ObjectiveMetric? ObjectiveMetric,
        OutcomeMeasureResult? OutcomeMeasure);
}
