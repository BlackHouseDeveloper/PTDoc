using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.Communication;
using PTDoc.Application.Compliance;
using PTDoc.Application.Intake;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class JwtIntakeInviteServiceTests
{
    [Fact]
    public async Task CreateInviteAsync_Rotation_Invalidates_Previous_Invite()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var service = CreateService(db);

        var firstInvite = await service.CreateInviteAsync(intake.Id);
        var secondInvite = await service.CreateInviteAsync(intake.Id);

        Assert.True(firstInvite.Success);
        Assert.True(secondInvite.Success);

        var firstValidation = await service.ValidateInviteTokenAsync(ReadInviteToken(firstInvite.InviteUrl!));
        var secondValidation = await service.ValidateInviteTokenAsync(ReadInviteToken(secondInvite.InviteUrl!));

        Assert.False(firstValidation.IsValid);
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

        var sent = await service.SendOtpAsync("patient@example.com", OtpChannel.Email);

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

    private static string ReadInviteToken(string inviteUrl)
    {
        var inviteQueryPart = new Uri(inviteUrl).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First(part => part.StartsWith("invite=", StringComparison.OrdinalIgnoreCase));

        return Uri.UnescapeDataString(inviteQueryPart["invite=".Length..]);
    }
}
