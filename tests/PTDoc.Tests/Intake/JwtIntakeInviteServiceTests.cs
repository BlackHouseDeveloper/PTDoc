using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.Communication;
using PTDoc.Application.Compliance;
using PTDoc.Application.Intake;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class JwtIntakeInviteServiceTests
{
    [Fact]
    public async Task CreateInviteAsync_ActiveInvite_IsIdempotent()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var service = CreateService(db);

        var firstInvite = await service.CreateInviteAsync(intake.Id);
        var secondInvite = await service.CreateInviteAsync(intake.Id);

        Assert.True(firstInvite.Success);
        Assert.True(secondInvite.Success);

        Assert.Equal(firstInvite.InviteUrl, secondInvite.InviteUrl);

        var firstValidation = await service.ValidateInviteTokenAsync(ReadInviteToken(firstInvite.InviteUrl!));
        var secondValidation = await service.ValidateInviteTokenAsync(ReadInviteToken(secondInvite.InviteUrl!));

        Assert.True(firstValidation.IsValid);
        Assert.True(secondValidation.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(secondValidation.AccessToken));
        Assert.NotNull(secondValidation.ExpiresAt);

        var stored = await db.IntakeForms.SingleAsync(form => form.Id == intake.Id);
        Assert.False(string.IsNullOrWhiteSpace(stored.AccessToken));
        Assert.NotNull(stored.ExpiresAt);
    }

    [Fact]
    public async Task CreateInviteAsync_OnLockedIntake_ReturnsFailure()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        intake.IsLocked = true;
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.CreateInviteAsync(intake.Id);

        Assert.False(result.Success);
        Assert.Contains("locked", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendOtpAsync_Email_UsesSharedEmailProvider_And_AuditsMaskedDestination()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var communicationService = new Mock<ICommunicationService>();
        communicationService
            .Setup(service => service.SendIntakeOtpEmailAsync(It.IsAny<IntakeOtpDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult
            {
                Succeeded = true,
                Status = DeliveryStatus.Sent,
                Provider = "AzureCommunicationServices",
                ProviderMessageId = "acs-email-otp",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Email,
                Purpose = DeliveryPurpose.IntakeOtp
            });

        communicationService
            .Setup(service => service.SendIntakeOtpSmsAsync(It.IsAny<IntakeOtpDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("SMS should not be called."));

        var auditService = new AuditService(db);
        var service = CreateService(db, communicationService: communicationService, auditService: auditService);

        var invite = await service.CreateInviteAsync(intake.Id);
        var sent = await service.SendOtpAsync(ReadInviteToken(invite.InviteUrl!), "patient@example.com", OtpChannel.Email);

        Assert.True(sent);
        communicationService.Verify(service => service.SendIntakeOtpEmailAsync(
            It.Is<IntakeOtpDeliveryRequest>(request =>
                request.Recipient == "patient@example.com" &&
                request.OtpCode.Length == 6),
            It.IsAny<CancellationToken>()),
            Times.Once);
        communicationService.Verify(service => service.SendIntakeOtpSmsAsync(
            It.IsAny<IntakeOtpDeliveryRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        var audit = await db.AuditLogs.SingleAsync(log => log.EventType == "IntakeOtpDelivered");
        Assert.DoesNotContain("patient@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p***t@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendOtpAsync_MismatchedInviteContact_DoesNotSend()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var communicationService = new Mock<ICommunicationService>();
        var service = CreateService(db, communicationService: communicationService);
        var invite = await service.CreateInviteAsync(intake.Id);

        var sent = await service.SendOtpAsync(ReadInviteToken(invite.InviteUrl!), "other@example.com", OtpChannel.Email);

        Assert.False(sent);
        communicationService.Verify(service => service.SendIntakeOtpEmailAsync(
            It.IsAny<IntakeOtpDeliveryRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendOtpAsync_DeliveryFailure_DoesNotLeaveUsableChallenge()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        string? otpCode = null;
        var communicationService = new Mock<ICommunicationService>();
        communicationService
            .Setup(service => service.SendIntakeOtpEmailAsync(It.IsAny<IntakeOtpDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntakeOtpDeliveryRequest, CancellationToken>((request, _) => otpCode = request.OtpCode)
            .ReturnsAsync(new DeliveryResult
            {
                Succeeded = false,
                Status = DeliveryStatus.Failed,
                Provider = "Fake",
                ErrorCode = "FakeOtpFailure",
                SafeErrorMessage = "Delivery failed.",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Email,
                Purpose = DeliveryPurpose.IntakeOtp
            });

        var service = CreateService(db, communicationService: communicationService);
        var invite = await service.CreateInviteAsync(intake.Id);
        var inviteToken = ReadInviteToken(invite.InviteUrl!);

        var sent = await service.SendOtpAsync(inviteToken, "patient@example.com", OtpChannel.Email);

        Assert.False(sent);
        Assert.False(string.IsNullOrWhiteSpace(otpCode));

        var verified = await service.VerifyOtpAndIssueAccessTokenAsync(
            inviteToken,
            "patient@example.com",
            OtpChannel.Email,
            otpCode!);
        Assert.False(verified.IsValid);

        var challenge = await db.IntakeOtpChallenges.SingleAsync();
        Assert.NotNull(challenge.ConsumedAtUtc);
        Assert.True(challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task VerifyOtpAndIssueAccessTokenAsync_ConcurrentValidCode_IsSingleUse()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ptdoc-otp-{Guid.NewGuid():N}.db");
        try
        {
            string inviteToken;
            string? otpCode = null;
            await using (var setupDb = CreateSqliteDbContext(dbPath))
            {
                await setupDb.Database.EnsureCreatedAsync();
                var intake = await SeedOpenIntakeAsync(setupDb);
                var communicationService = new Mock<ICommunicationService>();
                communicationService
                    .Setup(service => service.SendIntakeOtpEmailAsync(It.IsAny<IntakeOtpDeliveryRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<IntakeOtpDeliveryRequest, CancellationToken>((request, _) => otpCode = request.OtpCode)
                    .ReturnsAsync(new DeliveryResult
                    {
                        Succeeded = true,
                        Status = DeliveryStatus.Sent,
                        Provider = "Fake",
                        SentAtUtc = DateTimeOffset.UtcNow,
                        Channel = DeliveryChannel.Email,
                        Purpose = DeliveryPurpose.IntakeOtp
                    });

                var setupService = CreateService(setupDb, communicationService: communicationService);
                var invite = await setupService.CreateInviteAsync(intake.Id);
                inviteToken = ReadInviteToken(invite.InviteUrl!);
                Assert.True(await setupService.SendOtpAsync(inviteToken, "patient@example.com", OtpChannel.Email));
            }

            Assert.False(string.IsNullOrWhiteSpace(otpCode));

            await using var firstDb = CreateSqliteDbContext(dbPath);
            await using var secondDb = CreateSqliteDbContext(dbPath);
            var firstService = CreateService(firstDb);
            var secondService = CreateService(secondDb);

            var results = await Task.WhenAll(
                firstService.VerifyOtpAndIssueAccessTokenAsync(inviteToken, "patient@example.com", OtpChannel.Email, otpCode!),
                secondService.VerifyOtpAndIssueAccessTokenAsync(inviteToken, "patient@example.com", OtpChannel.Email, otpCode!));

            Assert.Single(results.Where(result => result.IsValid));
            Assert.Single(results.Where(result => !result.IsValid));

            await using var verifyDb = CreateSqliteDbContext(dbPath);
            var challenge = await verifyDb.IntakeOtpChallenges.SingleAsync();
            Assert.NotNull(challenge.ConsumedAtUtc);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static JwtIntakeInviteService CreateService(
        ApplicationDbContext db,
        Mock<ICommunicationService>? communicationService = null,
        IAuditService? auditService = null)
    {
        return new JwtIntakeInviteService(
            Options.Create(new IntakeInviteOptions
            {
                SigningKey = "unit-test-intake-signing-key-that-is-well-over-thirty-two-chars",
                InviteExpiryMinutes = 1440,
                AccessTokenExpiryMinutes = 120,
                OtpExpiryMinutes = 10,
                PublicWebBaseUrl = "http://localhost"
            }),
            db,
            (communicationService ?? new Mock<ICommunicationService>()).Object,
            new ContactNormalizer(),
            auditService ?? Mock.Of<IAuditService>(),
            Mock.Of<ILogger<JwtIntakeInviteService>>());
    }

    private static async Task<IntakeForm> SeedOpenIntakeAsync(ApplicationDbContext db)
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Pat",
            LastName = "Ient",
            DateOfBirth = new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
            Email = "patient@example.com",
            Phone = "5551234567"
        };

        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            TemplateVersion = "1.0",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = """{"hipaaAcknowledged":true}""",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.Empty,
            SyncState = SyncState.Pending,
            IsLocked = false
        };

        db.Patients.Add(patient);
        db.IntakeForms.Add(intake);
        await db.SaveChangesAsync();
        return intake;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ApplicationDbContext CreateSqliteDbContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string ReadInviteToken(string inviteUrl)
    {
        var inviteQueryPart = new Uri(inviteUrl).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First(part => part.StartsWith("invite=", StringComparison.OrdinalIgnoreCase));

        return Uri.UnescapeDataString(inviteQueryPart["invite=".Length..]);
    }
}
