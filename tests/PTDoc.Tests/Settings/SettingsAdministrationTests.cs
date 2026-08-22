using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Api.Auth;
using PTDoc.Api.Security;
using PTDoc.Api.Settings;
using PTDoc.Application.Communication;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Settings;
using Xunit;

namespace PTDoc.Tests.Settings;

[Trait("Category", "CoreCi")]
public sealed class SettingsAdministrationTests
{
    [Fact]
    public void CanonicalPermissionCatalog_HasStableCompleteMatrixAndRecoveryLocks()
    {
        Assert.Equal(30, RolePermissionCatalog.Capabilities.Count);
        Assert.Equal(9, RolePermissionCatalog.Roles.Count);
        Assert.Equal(
            Enumerable.Range(1, 30),
            RolePermissionCatalog.Capabilities.Select(capability => (int)capability.Key));

        Assert.True(RolePermissionCatalog.FindRole("Owner")!.IsReadOnly);
        Assert.Equal(PermissionLevel.Full,
            RolePermissionCatalog.GetLockedMinimum("Admin", CapabilityKey.UsersManage));
        Assert.Equal(PermissionLevel.Full,
            RolePermissionCatalog.GetLockedMinimum("Admin", CapabilityKey.RolesPermissionsManage));
        Assert.Equal(PermissionLevel.None,
            RolePermissionCatalog.GetCanonicalLevel("Patient", CapabilityKey.ClinicalNotesView));
    }

    [Theory]
    [InlineData("mfa", true)]
    [InlineData("[\"pwd\",\"mfa\"]", true)]
    [InlineData("pwd", false)]
    [InlineData("c1", false)]
    public void ExternalMfaAssurance_AcceptsOnlyExplicitVerifiedMfaMethod(string amr, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("amr", amr)], "test"));

        Assert.Equal(expected, ExternalMfaAssuranceMiddleware.HasVerifiedMfaMethod(principal));
    }

    [Fact]
    public void SettingsSecretProtector_NullEnvelopeFailsClosed()
    {
        var provider = new EphemeralDataProtectionProvider();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var protector = new DataProtectionSettingsSecretProtector(provider, time);
        var protectedNull = provider
            .CreateProtector("PTDoc.Settings.Security.v1", "challenge")
            .Protect("null");

        var succeeded = protector.TryUnprotect(
            "challenge",
            protectedNull,
            TimeSpan.FromMinutes(5),
            out var plaintext);

        Assert.False(succeeded);
        Assert.Empty(plaintext);
    }

    [Fact]
    public async Task DynamicCapabilityAuthorization_PropagatesRequestCancellation()
    {
        var clinicId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        var tenantContext = new Mock<ITenantContextAccessor>();
        tenantContext.Setup(accessor => accessor.GetCurrentClinicId()).Returns(clinicId);
        var evaluator = new Mock<IPermissionEvaluator>();
        evaluator.Setup(service => service.EvaluateAsync(
                clinicId,
                Roles.Admin,
                CapabilityKey.ClinicSettingsManage,
                PermissionLevel.View,
                true,
                cancellation.Token))
            .ReturnsAsync(new PermissionEvaluation(true, true, true, AuthorizationRolloutMode.Enforced, "allowed"));
        var requirement = new DynamicCapabilityRequirement(
            [CapabilityKey.ClinicSettingsManage],
            PermissionLevel.View,
            [Roles.Admin]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.Admin)],
            "test"));
        var authorizationContext = new AuthorizationHandlerContext(
            [requirement],
            principal,
            httpContext);

        await new DynamicCapabilityAuthorizationHandler(tenantContext.Object, evaluator.Object)
            .HandleAsync(authorizationContext);

        Assert.True(authorizationContext.HasSucceeded);
        evaluator.VerifyAll();
    }

    [Fact]
    public async Task NewClinic_IsSeededWithVersionedTenantSettings()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Seeded Clinic", Slug = $"seed-{Guid.NewGuid():N}" };

        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        Assert.Equal("America/Los_Angeles", clinic.TimeZoneId);
        Assert.Equal(12, await context.VisitTypes.CountAsync(item => item.ClinicId == clinic.Id));
        Assert.Equal(7, await context.ClinicBusinessHours.CountAsync(item => item.ClinicId == clinic.Id));
        Assert.Equal(270, await context.RoleCapabilityPermissions.CountAsync(item => item.ClinicId == clinic.Id));
        Assert.Single(await context.ClinicSecurityPolicies.Where(item => item.ClinicId == clinic.Id).ToListAsync());
        Assert.Single(await context.SchedulingPreferences.Where(item => item.ClinicId == clinic.Id).ToListAsync());
        Assert.Single(await context.AutoCheckInPolicies.Where(item => item.ClinicId == clinic.Id).ToListAsync());

        var unsupported = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id && item.RoleKey == "PT" &&
            item.CapabilityKey == CapabilityKey.StaffMessagesSend);
        Assert.Equal(PermissionLevel.None, unsupported.Level);
    }

    [Fact]
    public async Task RoleAdministration_RejectsOwnerMutationAndLockedAdminReduction()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Permissions Clinic", Slug = $"permissions-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new RolePermissionAdministrationService(context, CreateAuditService().Object);

        var ownerResult = await service.UpdateAsync(
            clinic.Id,
            "Owner",
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.ClinicSettingsManage, PermissionLevel.Full, 1)]),
            Guid.NewGuid(),
            "owner-read-only");
        var adminResult = await service.UpdateAsync(
            clinic.Id,
            "Admin",
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.UsersManage, PermissionLevel.View, 1)]),
            Guid.NewGuid(),
            "admin-recovery-lock");

        Assert.Equal(SettingsOperationStatus.Forbidden, ownerResult.Status);
        Assert.Equal("role_read_only", ownerResult.ErrorCode);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, adminResult.Status);
        Assert.Contains("permissions.UsersManage", adminResult.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task SchedulingPolicy_UsesClinicIanaTimeZoneAcrossDstTransition()
    {
        await using var context = CreateContext();
        var clinic = new Clinic
        {
            Name = "Eastern Clinic",
            Slug = $"eastern-{Guid.NewGuid():N}",
            TimeZoneId = "America/New_York"
        };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var sunday = await context.ClinicBusinessHours.SingleAsync(item =>
            item.ClinicId == clinic.Id && item.DayOfWeek == DayOfWeek.Sunday);
        sunday.IsOpen = true;
        sunday.StartLocalTime = new TimeOnly(1, 0);
        sunday.EndLocalTime = new TimeOnly(4, 0);
        sunday.LunchStartLocalTime = null;
        sunday.LunchEndLocalTime = null;
        (await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id))
            .AppointmentBufferMinutes = 0;
        var clinicianId = Guid.NewGuid();
        context.Appointments.Add(new Appointment
        {
            PatientId = Guid.NewGuid(),
            ClinicId = clinic.Id,
            ClinicalId = clinicianId,
            StartTimeUtc = new DateTime(2026, 3, 8, 6, 45, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 3, 8, 7, 15, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.NoShow,
            LastModifiedUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var service = new SchedulingPolicyEvaluator(context);
        var result = await service.EvaluateAsync(new AvailabilityRequest(
            clinic.Id,
            clinicianId,
            new DateTime(2026, 3, 8, 6, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc)));

        Assert.True(result.IsAvailable);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public async Task TotpEnrollment_IssuesRecoveryCodesAndRejectsReplay()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "MFA Clinic", Slug = $"mfa-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "mfa-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Mfa",
            LastName = "Admin",
            Role = "Admin",
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.Clinics.Add(clinic);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var service = new MfaAuthenticationService(context, new TestSecretProtector(), CreateAuditService().Object, time);
        var enrollmentLoginChallenge = service.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment);
        var start = await service.BeginEnrollmentAsync(enrollmentLoginChallenge);
        Assert.True(start.Succeeded);

        var enrollmentCode = ComputeTotp(DecodeBase32(start.Value!.ManualKey), time.GetUtcNow());
        var completion = await service.VerifyEnrollmentAsync(start.Value.EnrollmentChallengeToken, enrollmentCode);
        Assert.True(completion.Succeeded);
        Assert.Equal(10, completion.Value!.RecoveryCodes.Count);
        Assert.Equal(10, await context.UserMfaRecoveryCodes.CountAsync());

        time.Advance(TimeSpan.FromSeconds(30));
        var verificationChallenge = service.CreateChallenge(user.Id, MfaChallengePurpose.Verification);
        var verificationCode = ComputeTotp(DecodeBase32(start.Value.ManualKey), time.GetUtcNow());
        var first = await service.VerifyAsync(verificationChallenge, verificationCode);
        var replay = await service.VerifyAsync(verificationChallenge, verificationCode);

        Assert.True(first.Succeeded);
        Assert.False(replay.Succeeded);
        Assert.Equal("invalid_code", replay.ErrorCode);
    }

    [Fact]
    public async Task TotpRecoveryCodeRegeneration_InvalidatesPriorSet()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "MFA Recovery Clinic", Slug = $"mfa-recovery-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "mfa-recovery-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Mfa",
            LastName = "Recovery",
            Role = "Admin",
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();

        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var service = new MfaAuthenticationService(context, new TestSecretProtector(), CreateAuditService().Object, time);
        var start = await service.BeginEnrollmentAsync(service.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment));
        var secret = DecodeBase32(start.Value!.ManualKey);
        var completion = await service.VerifyEnrollmentAsync(
            start.Value.EnrollmentChallengeToken,
            ComputeTotp(secret, time.GetUtcNow()));
        var priorRecoveryCode = completion.Value!.RecoveryCodes[0];

        time.Advance(TimeSpan.FromSeconds(30));
        var regenerated = await service.RegenerateRecoveryCodesAsync(
            user.Id,
            ComputeTotp(secret, time.GetUtcNow()));

        Assert.True(regenerated.Succeeded);
        Assert.Equal(10, regenerated.Value!.RecoveryCodes.Count);
        Assert.Equal(10, await context.UserMfaRecoveryCodes.CountAsync());

        var priorResult = await service.RecoverAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Verification),
            priorRecoveryCode);
        var newResult = await service.RecoverAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Verification),
            regenerated.Value.RecoveryCodes[0]);

        Assert.False(priorResult.Succeeded);
        Assert.True(newResult.Succeeded);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_QueuesConsentedChannelsOnlyOnce()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Reminder Clinic", Slug = $"reminder-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Reminder",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = "reminder@example.invalid",
            Phone = "+15555550100",
            ConsentSigned = true,
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid(),
            LastModifiedUtc = now.UtcDateTime
        };
        var appointment = new Appointment
        {
            PatientId = patient.Id,
            Patient = patient,
            ClinicalId = Guid.NewGuid(),
            ClinicId = clinic.Id,
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var intake = new IntakeForm
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            TemplateVersion = "1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = IntakeConsentJson.Serialize(new IntakeConsentPacket
            {
                CommunicationEmailConsent = true,
                CommunicationEmail = patient.Email,
                CommunicationTextConsent = false,
                CommunicationPhoneNumber = patient.Phone
            }),
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.AddRange(clinic, patient, appointment, intake);
        await context.SaveChangesAsync();

        AppointmentReminderDeliveryRequest? deliveredRequest = null;
        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentReminderDeliveryRequest, CancellationToken>((request, _) => deliveredRequest = request)
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        communication.Setup(service => service.SendAppointmentReminderSmsAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();
        await processor.ProcessDueAsync();

        var dispatches = await context.AppointmentReminderDispatches.ToListAsync();
        Assert.Single(dispatches);
        Assert.All(dispatches, dispatch => Assert.Equal(ReminderDispatchStatus.Sent, dispatch.Status));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        Assert.Contains(zone.DaylightName, deliveredRequest!.AppointmentLocalTime, StringComparison.Ordinal);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        communication.Verify(service => service.SendAppointmentReminderSmsAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AutoCheckInProcessor_QueuesMaximumLeadWithoutCompletedIntake()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Auto Check-In Clinic", Slug = $"auto-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var visitType = await context.VisitTypes.FirstAsync(item =>
            item.ClinicId == clinic.Id && item.RequiresIntake && item.IsActive);
        var scheduling = await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id);
        scheduling.SendAppointmentReminders = false;
        var policy = await context.AutoCheckInPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.IsEnabled = true;
        policy.LeadHours = 168;
        policy.EnableEmail = true;
        policy.EnableSms = false;
        policy.EligibleVisitTypeIdsJson = JsonSerializer.Serialize(new[] { visitType.Id });

        var eligiblePatient = CreateConsentedPatient(clinic.Id, "eligible@example.invalid", now.UtcDateTime);
        var completedPatient = CreateConsentedPatient(clinic.Id, "completed@example.invalid", now.UtcDateTime);
        var eligibleAppointment = CreateIntakeAppointment(clinic.Id, eligiblePatient, visitType, now.UtcDateTime.AddHours(167));
        var completedAppointment = CreateIntakeAppointment(clinic.Id, completedPatient, visitType, now.UtcDateTime.AddHours(167));
        var eligibleIntake = CreateConsentedIntake(clinic.Id, eligiblePatient, now.UtcDateTime, submittedAt: null);
        var completedIntake = CreateConsentedIntake(clinic.Id, completedPatient, now.UtcDateTime, submittedAt: now.UtcDateTime);
        context.AddRange(
            eligiblePatient,
            completedPatient,
            eligibleAppointment,
            completedAppointment,
            eligibleIntake,
            completedIntake);
        await context.SaveChangesAsync();

        var intakeWorkflow = new Mock<IIntakeCommunicationWorkflow>();
        intakeWorkflow.Setup(service => service.SendInviteAsync(
                It.IsAny<IntakeSendInviteRequest>(),
                It.IsAny<IntakeCommunicationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeSendInviteRequest request, IntakeCommunicationContext? _, CancellationToken _) =>
                new IntakeDeliverySendResult
                {
                    Success = true,
                    IntakeId = request.IntakeId,
                    PatientId = eligiblePatient.Id,
                    Channel = request.Channel
                });
        var processor = new AppointmentCommunicationProcessor(
            context,
            Mock.Of<ICommunicationService>(),
            intakeWorkflow.Object,
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        var dispatch = await context.AppointmentReminderDispatches.SingleAsync();
        Assert.Equal(eligibleAppointment.Id, dispatch.AppointmentId);
        Assert.Equal(ReminderDispatchPurpose.AutoCheckIn, dispatch.Purpose);
        Assert.Equal(ReminderDispatchStatus.Sent, dispatch.Status);
        Assert.DoesNotContain(
            await context.AppointmentReminderDispatches.ToListAsync(),
            item => item.AppointmentId == completedAppointment.Id);
        intakeWorkflow.Verify(service => service.SendInviteAsync(
            It.IsAny<IntakeSendInviteRequest>(),
            It.IsAny<IntakeCommunicationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_QueuesCandidatesBeyondFirstPage()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Paged Reminder Clinic", Slug = $"paged-{Guid.NewGuid():N}" };
        var patient = CreateConsentedPatient(clinic.Id, "paged@example.invalid", now.UtcDateTime);
        var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
        context.AddRange(clinic, patient, intake);
        for (var index = 0; index < 501; index++)
        {
            context.Appointments.Add(new Appointment
            {
                Patient = patient,
                PatientId = patient.Id,
                ClinicId = clinic.Id,
                ClinicalId = Guid.NewGuid(),
                StartTimeUtc = now.UtcDateTime.AddHours(23).AddSeconds(index),
                EndTimeUtc = now.UtcDateTime.AddHours(24).AddSeconds(index),
                Status = AppointmentStatus.Scheduled,
                LastModifiedUtc = now.UtcDateTime,
                ModifiedByUserId = Guid.NewGuid()
            });
        }
        await context.SaveChangesAsync();

        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        Assert.Equal(501, await context.AppointmentReminderDispatches.CountAsync());
    }

    [Fact]
    public async Task AppointmentReminderProcessor_AtomicallyClaimsDispatchAcrossWorkers()
    {
        var databaseName = $"settings-claims-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        Guid dispatchId;

        await using (var seedContext = new ApplicationDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var clinic = new Clinic { Name = "Claim Clinic", Slug = $"claim-{Guid.NewGuid():N}" };
            seedContext.Clinics.Add(clinic);
            await seedContext.SaveChangesAsync();
            (await seedContext.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id))
                .SendAppointmentReminders = false;
            (await seedContext.AutoCheckInPolicies.SingleAsync(item => item.ClinicId == clinic.Id))
                .IsEnabled = false;
            var patient = CreateConsentedPatient(clinic.Id, "claim@example.invalid", now.UtcDateTime);
            var appointment = new Appointment
            {
                Patient = patient,
                PatientId = patient.Id,
                Clinic = clinic,
                ClinicId = clinic.Id,
                ClinicalId = Guid.NewGuid(),
                StartTimeUtc = now.UtcDateTime.AddHours(24),
                EndTimeUtc = now.UtcDateTime.AddHours(25),
                Status = AppointmentStatus.Scheduled,
                LastModifiedUtc = now.UtcDateTime,
                ModifiedByUserId = Guid.NewGuid()
            };
            var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
            var dispatch = new AppointmentReminderDispatch
            {
                ClinicId = clinic.Id,
                Appointment = appointment,
                AppointmentId = appointment.Id,
                AppointmentVersionUtc = appointment.LastModifiedUtc,
                Purpose = ReminderDispatchPurpose.AppointmentReminder,
                Channel = ReminderChannel.Email,
                IdempotencyKey = $"claim:{appointment.Id:N}",
                Status = ReminderDispatchStatus.Pending,
                EligibleAtUtc = now.UtcDateTime,
                NextAttemptAtUtc = now.UtcDateTime,
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime
            };
            dispatchId = dispatch.Id;
            seedContext.AddRange(patient, appointment, intake, dispatch);
            await seedContext.SaveChangesAsync();
        }

        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        var first = new AppointmentCommunicationProcessor(
            firstContext,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);
        var second = new AppointmentCommunicationProcessor(
            secondContext,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await Task.WhenAll(first.ProcessDueAsync(), second.ProcessDueAsync());

        await using var assertionContext = new ApplicationDbContext(options);
        var saved = await assertionContext.AppointmentReminderDispatches.SingleAsync(item => item.Id == dispatchId);
        Assert.Equal(ReminderDispatchStatus.Sent, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_SuppressesDispatchAfterChannelConsentIsRevoked()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Revocation Clinic", Slug = $"revoke-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Revoked",
            LastName = "Consent",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = "revoked@example.invalid",
            ConsentSigned = true,
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid()
        };
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            Clinic = clinic,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var intake = new IntakeForm
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            TemplateVersion = "1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = IntakeConsentJson.Serialize(new IntakeConsentPacket
            {
                CommunicationEmailConsent = true,
                CommunicationEmail = patient.Email,
                RevokedConsentKeys = ["communicationEmailConsent"]
            }),
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var dispatch = new AppointmentReminderDispatch
        {
            ClinicId = clinic.Id,
            Appointment = appointment,
            AppointmentId = appointment.Id,
            AppointmentVersionUtc = appointment.LastModifiedUtc,
            Purpose = ReminderDispatchPurpose.AppointmentReminder,
            Channel = ReminderChannel.Email,
            IdempotencyKey = $"revoked:{appointment.Id:N}",
            Status = ReminderDispatchStatus.Pending,
            EligibleAtUtc = now.UtcDateTime,
            NextAttemptAtUtc = now.UtcDateTime,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };
        context.AddRange(clinic, patient, appointment, intake, dispatch);
        await context.SaveChangesAsync();
        var communication = new Mock<ICommunicationService>();
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        Assert.Equal(ReminderDispatchStatus.Suppressed, dispatch.Status);
        Assert.Equal("communication_consent_unavailable", dispatch.LastStatusCode);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KioskCheckIn_AcceptsAdvertisedNumericCodeAndQrPayload()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Kiosk Clinic", Slug = $"kiosk-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Kiosk",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid()
        };
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            Status = AppointmentStatus.Scheduled,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.AddRange(clinic, patient, appointment);
        await context.SaveChangesAsync();

        AuditEvent? tokenCreatedAudit = null;
        var audit = CreateAuditService();
        audit.Setup(service => service.LogSettingsEventAsync(
                It.Is<AuditEvent>(item => item.EventType == "KioskCheckInTokenCreated"),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEvent, CancellationToken>((item, _) => tokenCreatedAudit = item)
            .Returns(Task.CompletedTask);
        var service = new KioskCheckInService(
            context,
            audit.Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await service.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Front Desk iPad"),
            Guid.NewGuid(),
            "create-station");
        var enrollment = await service.EnrollAsync(station.Value!.Code);

        var numericToken = await service.CreateCheckInTokenAsync(
            clinic.Id, appointment.Id, Guid.NewGuid(), "numeric-token");
        var numericResult = await service.CheckInAsync(
            enrollment.Value!.DeviceCredential,
            numericToken.Value!.NumericCode);

        Assert.True(numericResult.Succeeded);
        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.NotNull(tokenCreatedAudit);
        Assert.Equal(nameof(KioskCheckInToken), tokenCreatedAudit.EntityType);
        Assert.NotEqual(appointment.Id, tokenCreatedAudit.EntityId);
        Assert.Equal(appointment.Id, tokenCreatedAudit.Metadata["appointmentId"]);

        var qrToken = await service.CreateCheckInTokenAsync(
            clinic.Id, appointment.Id, Guid.NewGuid(), "qr-token");
        var qrResult = await service.CheckInAsync(
            enrollment.Value.DeviceCredential,
            qrToken.Value!.QrPayload);

        Assert.True(qrResult.Succeeded);
        Assert.Equal(2, await context.KioskCheckInTokens.CountAsync(item => item.ConsumedAtUtc != null));
    }

    private static Patient CreateConsentedPatient(Guid clinicId, string email, DateTime now) =>
        new()
        {
            FirstName = "Communication",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = email,
            ConsentSigned = true,
            ClinicId = clinicId,
            ModifiedByUserId = Guid.NewGuid(),
            LastModifiedUtc = now
        };

    private static Appointment CreateIntakeAppointment(
        Guid clinicId,
        Patient patient,
        VisitType visitType,
        DateTime startTimeUtc) =>
        new()
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinicId,
            ClinicalId = Guid.NewGuid(),
            VisitType = visitType,
            VisitTypeId = visitType.Id,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = startTimeUtc.AddMinutes(visitType.DurationMinutes),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = startTimeUtc.AddDays(-1),
            ModifiedByUserId = Guid.NewGuid()
        };

    private static IntakeForm CreateConsentedIntake(
        Guid clinicId,
        Patient patient,
        DateTime now,
        DateTime? submittedAt) =>
        new()
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinicId,
            TemplateVersion = "1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = IntakeConsentJson.Serialize(new IntakeConsentPacket
            {
                CommunicationEmailConsent = true,
                CommunicationEmail = patient.Email
            }),
            SubmittedAt = submittedAt,
            LastModifiedUtc = now,
            ModifiedByUserId = Guid.NewGuid()
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<IAuditService> CreateAuditService()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogSettingsEventAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return audit;
    }

    private static string ComputeTotp(byte[] secret, DateTimeOffset instant)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, instant.ToUnixTimeSeconds() / 30);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                     | ((hash[offset + 1] & 0xff) << 16)
                     | ((hash[offset + 2] & 0xff) << 8)
                     | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value)
        {
            var index = alphabet.IndexOf(character);
            Assert.True(index >= 0);
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            output.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return output.ToArray();
    }

    private sealed class TestSecretProtector : ISettingsSecretProtector
    {
        public string Protect(string purpose, string plaintext) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{purpose}\n{plaintext}"));

        public bool TryUnprotect(string purpose, string protectedValue, TimeSpan maximumAge, out string plaintext)
        {
            try
            {
                var value = Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
                var prefix = $"{purpose}\n";
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    plaintext = value[prefix.Length..];
                    return true;
                }
            }
            catch (FormatException)
            {
                // Invalid protected values fail closed.
            }

            plaintext = string.Empty;
            return false;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan duration) => _value += duration;
    }
}
