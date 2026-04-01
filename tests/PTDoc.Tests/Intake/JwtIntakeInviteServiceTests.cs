using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Integrations;
using PTDoc.Application.Intake;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "Intake")]
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
        var emailService = new Mock<IEmailDeliveryService>();
        emailService
            .Setup(service => service.SendAsync(It.IsAny<EmailDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailDeliveryResult
            {
                Success = true,
                ProviderMessageId = "sendgrid-otp"
            });

        var smsService = new Mock<ISmsDeliveryService>(MockBehavior.Strict);
        var auditService = new AuditService(db);
        var service = CreateService(db, emailService: emailService, smsService: smsService, auditService: auditService);

        var sent = await service.SendOtpAsync("patient@example.com", OtpChannel.Email);

        Assert.True(sent);
        emailService.Verify(service => service.SendAsync(
            It.Is<EmailDeliveryRequest>(request =>
                request.ToAddress == "patient@example.com" &&
                request.Subject.Contains("verification code", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()),
            Times.Once);
        smsService.VerifyNoOtherCalls();

        var audit = await db.AuditLogs.SingleAsync(log => log.EventType == "IntakeOtpDelivered");
        Assert.DoesNotContain("patient@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p***t@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    private static JwtIntakeInviteService CreateService(
        ApplicationDbContext db,
        Mock<IEmailDeliveryService>? emailService = null,
        Mock<ISmsDeliveryService>? smsService = null,
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
            (emailService ?? new Mock<IEmailDeliveryService>()).Object,
            (smsService ?? new Mock<ISmsDeliveryService>()).Object,
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
