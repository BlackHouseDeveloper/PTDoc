using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.Communication;
using PTDoc.Application.Intake;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
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

        var service = new IntakeDeliveryService(new IntakeCommunicationWorkflow(
            db,
            inviteService.Object,
            Mock.Of<ICommunicationService>(),
            new ContactNormalizer(),
            auditService,
            Options.Create(new CommunicationOptions())));

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
    public async Task GetDeliveryBundleAsync_WithPublicBaseUrlOverride_RewritesLoopbackInviteUrl()
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
                $"http://localhost:5000/intake/{intake.PatientId:D}?mode=patient&invite=test-token",
                DateTimeOffset.Parse("2026-03-31T20:00:00Z"),
                null));

        var workflow = new IntakeCommunicationWorkflow(
            db,
            inviteService.Object,
            Mock.Of<ICommunicationService>(),
            new ContactNormalizer(),
            auditService,
            Options.Create(new CommunicationOptions()));

        var bundle = await workflow.GetDeliveryBundleAsync(
            intake.Id,
            new IntakeCommunicationContext
            {
                PublicWebBaseUrlOverride = "https://0bh3gh9l-5145.use2.devtunnels.ms"
            });

        Assert.StartsWith("https://0bh3gh9l-5145.use2.devtunnels.ms/intake/", bundle.InviteUrl, StringComparison.Ordinal);
        Assert.Contains("invite=test-token", bundle.InviteUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", bundle.InviteUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg", bundle.QrSvg, StringComparison.OrdinalIgnoreCase);
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

        var communicationService = new Mock<ICommunicationService>();
        communicationService
            .Setup(service => service.SendIntakeLinkEmailAsync(It.IsAny<IntakeLinkDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult
            {
                Succeeded = true,
                Status = DeliveryStatus.Sent,
                Provider = "AzureCommunicationServices",
                ProviderMessageId = "acs-message-123",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Email,
                Purpose = DeliveryPurpose.IntakeLink
            });

        communicationService
            .Setup(service => service.SendIntakeLinkSmsAsync(It.IsAny<IntakeLinkDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("SMS should not be called."));

        var service = new IntakeDeliveryService(new IntakeCommunicationWorkflow(
            db,
            inviteService.Object,
            communicationService.Object,
            new ContactNormalizer(),
            auditService,
            Options.Create(new CommunicationOptions())));

        var sendResult = await service.SendInviteAsync(new IntakeSendInviteRequest
        {
            IntakeId = intake.Id,
            Channel = IntakeDeliveryChannel.Email
        });

        Assert.True(sendResult.Success);
        Assert.Equal("p***t@example.com", sendResult.DestinationMasked);
        Assert.Equal("acs-message-123", sendResult.ProviderMessageId);
        communicationService.Verify(service => service.SendIntakeLinkEmailAsync(
            It.Is<IntakeLinkDeliveryRequest>(request =>
                request.Recipient == "patient@example.com" &&
                request.PatientId == intake.PatientId &&
                request.InviteUrl.Contains("invite=test-token", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()),
            Times.Once);
        communicationService.Verify(service => service.SendIntakeLinkSmsAsync(
            It.IsAny<IntakeLinkDeliveryRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

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

    [Fact]
    public async Task SendInviteAsync_WithPublicBaseUrlOverride_DeliversPublicInviteUrl()
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
                $"http://localhost:5000/intake/{intake.PatientId:D}?mode=patient&invite=test-token",
                DateTimeOffset.UtcNow.AddHours(4),
                null));

        var communicationService = new Mock<ICommunicationService>();
        communicationService
            .Setup(service => service.SendIntakeLinkEmailAsync(It.IsAny<IntakeLinkDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult
            {
                Succeeded = true,
                Status = DeliveryStatus.Sent,
                Provider = "Fake",
                ProviderMessageId = "fake-message-123",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Email,
                Purpose = DeliveryPurpose.IntakeLink
            });

        var workflow = new IntakeCommunicationWorkflow(
            db,
            inviteService.Object,
            communicationService.Object,
            new ContactNormalizer(),
            auditService,
            Options.Create(new CommunicationOptions()));

        var sendResult = await workflow.SendInviteAsync(
            new IntakeSendInviteRequest
            {
                IntakeId = intake.Id,
                Channel = IntakeDeliveryChannel.Email
            },
            new IntakeCommunicationContext
            {
                PublicWebBaseUrlOverride = "https://0bh3gh9l-5145.use2.devtunnels.ms"
            });

        Assert.True(sendResult.Success);
        communicationService.Verify(service => service.SendIntakeLinkEmailAsync(
            It.Is<IntakeLinkDeliveryRequest>(request =>
                request.InviteUrl.StartsWith("https://0bh3gh9l-5145.use2.devtunnels.ms/intake/", StringComparison.Ordinal) &&
                request.InviteUrl.Contains("invite=test-token", StringComparison.Ordinal) &&
                !request.InviteUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()),
            Times.Once);
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
