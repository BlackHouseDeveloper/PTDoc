using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Integrations;
using PTDoc.Application.Intake;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "Intake")]
public sealed class IntakeDeliveryServiceTests
{
    [Fact]
    public async Task GetDeliveryBundleAsync_ReturnsLinkQrSvg_And_AuditsInviteCreation()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var auditService = new AuditService(db);
        var inviteService = new Mock<IIntakeInviteService>();
        inviteService
            .Setup(service => service.CreateInviteAsync(intake.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeInviteLinkResult(
                true,
                intake.Id,
                intake.PatientId,
                $"http://localhost/intake/{intake.PatientId:D}?mode=patient&invite=test-token",
                DateTimeOffset.Parse("2026-03-31T20:00:00Z"),
                null));

        var service = new IntakeDeliveryService(
            db,
            inviteService.Object,
            Mock.Of<IEmailDeliveryService>(),
            Mock.Of<ISmsDeliveryService>(),
            auditService);

        var bundle = await service.GetDeliveryBundleAsync(intake.Id);

        Assert.Equal(intake.Id, bundle.IntakeId);
        Assert.Contains("invite=test-token", bundle.InviteUrl, StringComparison.Ordinal);
        Assert.Contains("<svg", bundle.QrSvg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DateTimeOffset.Parse("2026-03-31T20:00:00Z"), bundle.ExpiresAt);

        var audit = await db.AuditLogs.SingleAsync(log => log.EventType == "IntakeInviteCreated");
        Assert.Equal(nameof(IntakeForm), audit.EntityType);
        Assert.Equal(intake.Id, audit.EntityId);
        Assert.DoesNotContain("patient@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendInviteAsync_UsesPatientFallbackEmail_And_StatusReflectsMaskedAuditHistory()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var auditService = new AuditService(db);

        var inviteService = new Mock<IIntakeInviteService>();
        inviteService
            .Setup(service => service.CreateInviteAsync(intake.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeInviteLinkResult(
                true,
                intake.Id,
                intake.PatientId,
                $"http://localhost/intake/{intake.PatientId:D}?mode=patient&invite=test-token",
                DateTimeOffset.UtcNow.AddHours(4),
                null));

        var emailService = new Mock<IEmailDeliveryService>();
        emailService
            .Setup(service => service.SendAsync(It.IsAny<EmailDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailDeliveryResult
            {
                Success = true,
                ProviderMessageId = "sendgrid-message-123"
            });

        var smsService = new Mock<ISmsDeliveryService>(MockBehavior.Strict);

        var service = new IntakeDeliveryService(
            db,
            inviteService.Object,
            emailService.Object,
            smsService.Object,
            auditService);

        var sendResult = await service.SendInviteAsync(new IntakeSendInviteRequest
        {
            IntakeId = intake.Id,
            Channel = IntakeDeliveryChannel.Email
        });

        Assert.True(sendResult.Success);
        Assert.Equal("p***t@example.com", sendResult.DestinationMasked);
        Assert.Equal("sendgrid-message-123", sendResult.ProviderMessageId);
        emailService.Verify(service => service.SendAsync(
            It.Is<EmailDeliveryRequest>(request =>
                request.ToAddress == "patient@example.com" &&
                request.Subject.Contains("intake form", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()),
            Times.Once);
        smsService.VerifyNoOtherCalls();

        var status = await service.GetDeliveryStatusAsync(intake.Id);
        Assert.True(status.InviteActive);
        Assert.NotNull(status.LastLinkGeneratedAt);
        Assert.NotNull(status.LastEmailSentAt);
        Assert.Equal("p***t@example.com", status.LastEmailDestinationMasked);

        var deliveredAudit = await db.AuditLogs
            .Where(log => log.EventType == "IntakeInviteDelivered")
            .SingleAsync();
        Assert.DoesNotContain("patient@example.com", deliveredAudit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p***t@example.com", deliveredAudit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IntakeForm> SeedOpenIntakeAsync(ApplicationDbContext db)
    {
        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            Name = "PFPT Downtown"
        };

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Pat",
            LastName = "Ient",
            DateOfBirth = new DateTime(1991, 6, 4, 0, 0, 0, DateTimeKind.Utc),
            Email = "patient@example.com",
            Phone = "5551234567",
            ClinicId = clinic.Id,
            Clinic = clinic
        };

        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            ClinicId = clinic.Id,
            Clinic = clinic,
            TemplateVersion = "1.0",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = """{"hipaaAcknowledged":true}""",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.Empty,
            SyncState = SyncState.Pending,
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };

        db.Clinics.Add(clinic);
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
}
