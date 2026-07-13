using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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
    private const string SigningKey = "unit-test-intake-signing-key-that-is-well-over-thirty-two-chars";

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
    public async Task SendOtpWithDiagnosticsAsync_Email_UsesSharedProviderAndOpaqueCorrelation()
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
        var result = await service.SendOtpWithDiagnosticsAsync(
            ReadInviteToken(invite.InviteUrl!),
            "patient@example.com",
            OtpChannel.Email);

        Assert.True(result.Success);
        Assert.Equal(IntakeOtpSendOutcome.Delivered, result.Outcome);
        Assert.Equal(32, result.RequestId.Length);
        communicationService.Verify(service => service.SendIntakeOtpEmailAsync(
            It.Is<IntakeOtpDeliveryRequest>(request =>
                request.Recipient == "patient@example.com" &&
                request.OtpCode.Length == 6 &&
                request.CorrelationId == result.RequestId),
            It.IsAny<CancellationToken>()),
            Times.Once);
        communicationService.Verify(service => service.SendIntakeOtpSmsAsync(
            It.IsAny<IntakeOtpDeliveryRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        var audit = await db.AuditLogs.SingleAsync(log => log.EventType == "IntakeOtpDelivered");
        Assert.Equal(result.RequestId, audit.CorrelationId);
        Assert.Equal(intake.Id, audit.EntityId);
        Assert.DoesNotContain("patient@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("p***t@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delivered", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendOtpAsync_MismatchedInviteContact_DoesNotSend()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var communicationService = new Mock<ICommunicationService>();
        var service = CreateService(
            db,
            communicationService: communicationService,
            auditService: new AuditService(db));
        var invite = await service.CreateInviteAsync(intake.Id);

        var result = await service.SendOtpWithDiagnosticsAsync(
            ReadInviteToken(invite.InviteUrl!),
            "other@example.com",
            OtpChannel.Email);

        Assert.False(result.Success);
        Assert.Equal(IntakeOtpSendOutcome.ContactMismatch, result.Outcome);
        communicationService.Verify(service => service.SendIntakeOtpEmailAsync(
            It.IsAny<IntakeOtpDeliveryRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
        var audit = await db.AuditLogs.SingleAsync(log => log.EventType == "IntakeOtpDeliveryFailed");
        Assert.Equal(result.RequestId, audit.CorrelationId);
        Assert.Equal(intake.Id, audit.EntityId);
        Assert.Contains("ContactMismatch", audit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("other@example.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("not.a.jwt")]
    public async Task InviteTokenValidation_MalformedToken_ReturnsSafeFailures(string inviteToken)
    {
        await using var db = CreateDbContext();
        var communicationService = new Mock<ICommunicationService>();
        var service = CreateService(db, communicationService: communicationService);

        var validation = await service.ValidateInviteTokenAsync(inviteToken);
        var sent = await service.SendOtpAsync(inviteToken, "patient@example.com", OtpChannel.Email);
        var verified = await service.VerifyOtpAndIssueAccessTokenAsync(
            inviteToken,
            "patient@example.com",
            OtpChannel.Email,
            "123456");
        var sessionValid = await service.ValidateAccessTokenAsync(inviteToken);
        await service.RevokeAccessTokenAsync(inviteToken);

        Assert.False(validation.IsValid);
        Assert.False(sent);
        Assert.False(verified.IsValid);
        Assert.False(sessionValid);
        communicationService.Verify(service => service.SendIntakeOtpEmailAsync(
            It.IsAny<IntakeOtpDeliveryRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("invalid-signature")]
    [InlineData("wrong-type")]
    public async Task InviteTokenValidation_InvalidJwt_ReturnsSafeFailures(string tokenKind)
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        intake.AccessToken = "stored-secret-hash";
        intake.ExpiresAt = DateTime.UtcNow.AddHours(1);
        await db.SaveChangesAsync();

        var inviteToken = tokenKind switch
        {
            "expired" => WriteJwt(
                signingKey: SigningKey,
                audience: "ptdoc_invite",
                tokenType: "intake_invite",
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                claims:
                [
                    new Claim("intake_id", intake.Id.ToString()),
                    new Claim("patient_id", intake.PatientId.ToString()),
                    new Claim("invite_secret", "raw-secret")
                ]),
            "invalid-signature" => WriteJwt(
                signingKey: "different-invalid-signing-key-that-is-well-over-thirty-two-chars",
                audience: "ptdoc_invite",
                tokenType: "intake_invite",
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
                claims:
                [
                    new Claim("intake_id", intake.Id.ToString()),
                    new Claim("patient_id", intake.PatientId.ToString()),
                    new Claim("invite_secret", "raw-secret")
                ]),
            "wrong-type" => WriteJwt(
                signingKey: SigningKey,
                audience: "ptdoc_invite",
                tokenType: "intake_access",
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
                claims:
                [
                    new Claim("intake_id", intake.Id.ToString()),
                    new Claim("patient_id", intake.PatientId.ToString()),
                    new Claim("invite_secret", "raw-secret")
                ]),
            _ => throw new InvalidOperationException($"Unhandled token kind {tokenKind}.")
        };

        var communicationService = new Mock<ICommunicationService>();
        var service = CreateService(db, communicationService: communicationService);

        var validation = await service.ValidateInviteTokenAsync(inviteToken);
        var sent = await service.SendOtpAsync(inviteToken, "patient@example.com", OtpChannel.Email);
        var verified = await service.VerifyOtpAndIssueAccessTokenAsync(
            inviteToken,
            "patient@example.com",
            OtpChannel.Email,
            "123456");

        Assert.False(validation.IsValid);
        Assert.False(sent);
        Assert.False(verified.IsValid);
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

        var result = await service.SendOtpWithDiagnosticsAsync(inviteToken, "patient@example.com", OtpChannel.Email);

        Assert.False(result.Success);
        Assert.Equal(IntakeOtpSendOutcome.ProviderRejected, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(otpCode));

        var verified = await service.VerifyOtpAndIssueAccessTokenAsync(
            inviteToken,
            "patient@example.com",
            OtpChannel.Email,
            otpCode!);
        Assert.False(verified.IsValid);

        var challenge = await db.IntakeOtpChallenges.SingleAsync();
        Assert.Equal(result.RequestId, challenge.CorrelationId);
        Assert.NotNull(challenge.ConsumedAtUtc);
        Assert.True(challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SendOtpWithDiagnosticsAsync_ExhaustedTransientProviderFailure_IsProviderOutage()
    {
        await using var db = CreateDbContext();
        var intake = await SeedOpenIntakeAsync(db);
        var communicationService = new Mock<ICommunicationService>();
        communicationService
            .Setup(service => service.SendIntakeOtpEmailAsync(It.IsAny<IntakeOtpDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult
            {
                Succeeded = false,
                Status = DeliveryStatus.Failed,
                Provider = "AzureCommunicationServices",
                ErrorCode = "Http503",
                SafeErrorMessage = "Email delivery failed.",
                RetryCount = 2,
                Channel = DeliveryChannel.Email,
                Purpose = DeliveryPurpose.IntakeOtp
            });
        var service = CreateService(db, communicationService: communicationService, auditService: new AuditService(db));
        var invite = await service.CreateInviteAsync(intake.Id);

        var result = await service.SendOtpWithDiagnosticsAsync(
            ReadInviteToken(invite.InviteUrl!),
            "patient@example.com",
            OtpChannel.Email);

        Assert.False(result.Success);
        Assert.Equal(IntakeOtpSendOutcome.ProviderOutage, result.Outcome);
        var audit = await db.AuditLogs.SingleAsync(log => log.EventType == "IntakeOtpDeliveryFailed");
        Assert.Equal(result.RequestId, audit.CorrelationId);
        Assert.Contains("ProviderOutage", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("Http503", audit.MetadataJson, StringComparison.Ordinal);
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
                SigningKey = SigningKey,
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

    private static string WriteJwt(
        string signingKey,
        string audience,
        string tokenType,
        DateTimeOffset expiresAt,
        IEnumerable<Claim> claims)
    {
        var jwtClaims = new List<Claim>(claims)
        {
            new("typ", tokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var now = DateTimeOffset.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: "PTDoc.IntakeInvite",
            audience: audience,
            claims: jwtClaims,
            notBefore: now.AddMinutes(-10).UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
