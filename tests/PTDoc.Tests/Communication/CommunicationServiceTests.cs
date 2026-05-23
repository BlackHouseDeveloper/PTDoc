using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.DependencyInjection;

namespace PTDoc.Tests.Communication;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class CommunicationServiceTests
{
    [Fact]
    public async Task PasswordResetEmail_HashesRecipientAndToken_AndDoesNotStoreRawContact()
    {
        await using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "clinician",
            PinHash = "hash",
            FirstName = "Clin",
            LastName = "Ician",
            Email = "clinician@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var service = CreateService(db, emailSender: emailSender);

        var result = await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "Clinician@Example.com",
            CorrelationId = "corr-1"
        });

        Assert.True(result.Succeeded);
        Assert.Single(emailSender.Messages);
        Assert.Equal("Reset your PTDoc PIN", emailSender.Messages[0].Subject);
        Assert.Contains("reset your PIN", emailSender.Messages[0].PlainTextBody, StringComparison.Ordinal);
        Assert.Contains("/reset-password?token=", emailSender.Messages[0].PlainTextBody, StringComparison.Ordinal);

        var token = await db.PasswordResetTokens.SingleAsync();
        Assert.Equal(user.Id, token.UserId);
        Assert.Equal(64, token.TokenHash.Length);
        Assert.DoesNotContain("token=", token.TokenHash, StringComparison.OrdinalIgnoreCase);

        var log = await db.CommunicationDeliveryLogs.SingleAsync();
        Assert.Equal(DeliveryPurpose.PasswordReset, log.Purpose);
        Assert.Equal(DeliveryChannel.Email, log.Channel);
        Assert.Equal(log.CreatedAtUtc.ToUnixTimeSeconds(), log.CreatedAtUnixSeconds);
        Assert.NotEqual("clinician@example.com", log.RecipientHash);
        Assert.DoesNotContain("clinician@example.com", log.RecipientHash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clinician@example.com", log.ProviderMessageId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=", log.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordResetEmail_WithPublicBaseUrlOverride_UsesPublicResetLink()
    {
        await using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "clinician",
            PinHash = "hash",
            FirstName = "Clin",
            LastName = "Ician",
            Email = "clinician@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var service = CreateService(db, emailSender: emailSender);

        var result = await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "clinician@example.com",
            PublicBaseUrlOverride = "https://0bh3gh9l-5145.use2.devtunnels.ms"
        });

        Assert.True(result.Succeeded);
        var body = Assert.Single(emailSender.Messages).PlainTextBody;
        Assert.Contains("https://0bh3gh9l-5145.use2.devtunnels.ms/reset-password?token=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordResetEmail_UnknownContact_AuditsSkippedRequestWithoutCreatingToken()
    {
        await using var db = CreateDbContext();
        var emailSender = new FakeEmailSender();
        var service = CreateService(db, emailSender: emailSender);

        var result = await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "unknown-reset@example.com",
            CorrelationId = "corr-unknown"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(DeliveryStatus.Skipped, result.Status);
        Assert.Empty(emailSender.Messages);
        Assert.Empty(await db.PasswordResetTokens.ToListAsync());

        var log = await db.CommunicationDeliveryLogs.SingleAsync();
        Assert.Equal(DeliveryPurpose.PasswordReset, log.Purpose);
        Assert.Equal(DeliveryStatus.Skipped, log.Status);
        Assert.Null(log.UserId);
        Assert.Equal("corr-unknown", log.CorrelationId);
        Assert.DoesNotContain("unknown-reset@example.com", log.RecipientHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordResetEmail_IsRecipientRateLimitedAfterConfiguredWindow()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, emailSender: new FakeEmailSender());

        for (var i = 0; i < 3; i++)
        {
            var accepted = await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
            {
                Recipient = "unknown-limited@example.com"
            });

            Assert.True(accepted.Succeeded);
            Assert.Equal(DeliveryStatus.Skipped, accepted.Status);
        }

        var limited = await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "unknown-limited@example.com"
        });

        Assert.False(limited.Succeeded);
        Assert.Equal(DeliveryStatus.RateLimited, limited.Status);
        Assert.Equal(4, await db.CommunicationDeliveryLogs.CountAsync());
        Assert.Contains(
            await db.CommunicationDeliveryLogs.ToListAsync(),
            log => log.Purpose == DeliveryPurpose.PasswordReset && log.Status == DeliveryStatus.RateLimited);
    }

    [Fact]
    public async Task IntakeSms_IsRateLimitedAfterFivePatientSendsPerDay()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, smsSender: new FakeSmsSender());
        var patientId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            var sent = await service.SendIntakeLinkSmsAsync(new IntakeLinkDeliveryRequest
            {
                PatientId = patientId,
                IntakeId = Guid.NewGuid(),
                Recipient = "555-010-0000",
                InviteUrl = $"http://localhost:5000/intake/{i}",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
            });

            Assert.True(sent.Succeeded);
        }

        var limited = await service.SendIntakeLinkSmsAsync(new IntakeLinkDeliveryRequest
        {
            PatientId = patientId,
            IntakeId = Guid.NewGuid(),
            Recipient = "555-010-0000",
            InviteUrl = "http://localhost:5000/intake/limited",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        });

        Assert.False(limited.Succeeded);
        Assert.Equal(DeliveryStatus.RateLimited, limited.Status);
        Assert.Equal(6, await db.CommunicationDeliveryLogs.CountAsync());
        Assert.DoesNotContain("555-010-0000", string.Join('\n', db.CommunicationDeliveryLogs.Select(log => log.RecipientHash)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordResetTokenService_ConsumesTokenOnlyOnce()
    {
        await using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "reset-user",
            PinHash = "old-hash",
            FirstName = "Reset",
            LastName = "User",
            Email = "reset@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var communicationService = CreateService(db, emailSender: emailSender);
        await communicationService.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "reset@example.com"
        });

        var link = emailSender.Messages.Single().PlainTextBody.Split(": ", StringSplitOptions.None).Last();
        var token = new Uri(link).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts => parts[0] == "token")?[1];
        token = token is null ? null : Uri.UnescapeDataString(token);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var resetService = new PasswordResetTokenService(db);
        var first = await resetService.ResetPinAsync(new PasswordResetCompletionRequest
        {
            Token = token!,
            NewPin = "1234"
        });

        var second = await resetService.ResetPinAsync(new PasswordResetCompletionRequest
        {
            Token = token!,
            NewPin = "5678"
        });

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(PasswordResetCompletionStatus.AlreadyUsed, second.Status);
        Assert.NotEqual("old-hash", (await db.Users.SingleAsync()).PinHash);
        Assert.NotNull(await db.PasswordResetTokens.Select(resetToken => resetToken.UsedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task PasswordResetTokenService_ValidateToken_InvalidInputs_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var resetService = new PasswordResetTokenService(db);

        var blank = await resetService.ValidateTokenAsync(new PasswordResetTokenValidationRequest { Token = "" });
        var unknown = await resetService.ValidateTokenAsync(new PasswordResetTokenValidationRequest { Token = "definitely-invalid" });
        var oversized = await resetService.ValidateTokenAsync(new PasswordResetTokenValidationRequest { Token = new string('x', 4097) });

        Assert.False(blank.IsValid);
        Assert.False(unknown.IsValid);
        Assert.False(oversized.IsValid);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("used")]
    [InlineData("revoked")]
    public async Task PasswordResetTokenService_ValidateToken_InactiveTokens_ReturnsFalse(string state)
    {
        await using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"reset-{state}",
            PinHash = "old-hash",
            FirstName = "Reset",
            LastName = "User",
            Email = $"reset-{state}@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var communicationService = CreateService(db, emailSender: emailSender);
        await communicationService.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = user.Email!
        });

        var token = ExtractResetToken(emailSender);
        var storedToken = await db.PasswordResetTokens.SingleAsync();
        switch (state)
        {
            case "expired":
                storedToken.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                break;
            case "used":
                storedToken.UsedAtUtc = DateTimeOffset.UtcNow;
                break;
            case "revoked":
                storedToken.RevokedAtUtc = DateTimeOffset.UtcNow;
                storedToken.RevocationReason = "TestRevocation";
                break;
        }
        await db.SaveChangesAsync();

        var resetService = new PasswordResetTokenService(db);
        var validation = await resetService.ValidateTokenAsync(new PasswordResetTokenValidationRequest { Token = token });

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task PasswordResetTokenService_ValidateToken_OldSchema_ReturnsFalse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE PasswordResetTokens (
                    Id TEXT NOT NULL PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    TokenHash TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    RecipientHash TEXT NOT NULL,
                    ExpiresAtUtc TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UsedAtUtc TEXT NULL,
                    CorrelationId TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        var resetService = new PasswordResetTokenService(db);

        var validation = await resetService.ValidateTokenAsync(new PasswordResetTokenValidationRequest
        {
            Token = "definitely-invalid"
        });

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task PasswordResetSms_DuplicateNormalizedPhone_FailsClosedWithoutSending()
    {
        await using var db = CreateDbContext();
        var sharedPhone = "+15551234567";
        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(),
                Username = "one",
                PinHash = "hash",
                FirstName = "One",
                LastName = "User",
                PhoneNumber = "(555) 123-4567",
                NormalizedPhoneNumber = sharedPhone,
                Role = "PT",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = Guid.NewGuid(),
                Username = "two",
                PinHash = "hash",
                FirstName = "Two",
                LastName = "User",
                PhoneNumber = "5551234567",
                NormalizedPhoneNumber = sharedPhone,
                Role = "PT",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var smsSender = new FakeSmsSender();
        var service = CreateService(db, smsSender: smsSender);

        var result = await service.SendPasswordResetSmsAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "555-123-4567"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(DeliveryStatus.Skipped, result.Status);
        Assert.Empty(smsSender.Messages);
        Assert.Empty(await db.PasswordResetTokens.ToListAsync());
    }

    [Fact]
    public async Task PasswordResetEmail_NewTokenRevokesPreviousActiveToken()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "reset-again",
            PinHash = "hash",
            FirstName = "Reset",
            LastName = "Again",
            Email = "reset-again@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, emailSender: new FakeEmailSender());

        await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest { Recipient = "reset-again@example.com" });
        await service.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest { Recipient = "reset-again@example.com" });

        var tokens = (await db.PasswordResetTokens.ToListAsync())
            .OrderBy(token => token.CreatedAtUtc)
            .ToList();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAtUtc);
        Assert.Equal("Superseded", tokens[0].RevocationReason);
        Assert.Null(tokens[1].RevokedAtUtc);
    }

    [Fact]
    public async Task PasswordResetEmail_WithSqlite_RevokesPreviousTokenWithoutDateTimeOffsetTranslationFailure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "sqlite-reset",
            PinHash = "hash",
            FirstName = "SQLite",
            LastName = "Reset",
            Email = "sqlite-reset@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var previousToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = new string('a', 64),
            RecipientHash = new string('b', 64),
            Channel = DeliveryChannel.Email,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(25)
        };
        db.Users.Add(user);
        db.PasswordResetTokens.Add(previousToken);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "sqlite-reset@example.com"
        });

        Assert.True(result.Succeeded);
        var tokens = (await db.PasswordResetTokens.ToListAsync())
            .OrderBy(token => token.CreatedAtUtc)
            .ToList();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAtUtc);
        Assert.Equal("Superseded", tokens[0].RevocationReason);
        Assert.Null(tokens[1].RevokedAtUtc);
    }

    [Fact]
    public async Task PasswordResetEmail_DeliveryFailureRevokesNewToken_AndKeepsPreviousTokenUsable()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "reset-delivery-failure",
            PinHash = "hash",
            FirstName = "Reset",
            LastName = "Failure",
            Email = "reset-delivery-failure@example.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var firstService = CreateService(db, emailSender: new FakeEmailSender());
        await firstService.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "reset-delivery-failure@example.com"
        });

        var failingService = CreateService(db, emailSender: new FailingEmailSender());
        var failed = await failingService.SendPasswordResetEmailAsync(new PasswordResetDeliveryRequest
        {
            Recipient = "reset-delivery-failure@example.com"
        });

        Assert.False(failed.Succeeded);
        Assert.Equal(DeliveryStatus.Failed, failed.Status);

        var tokens = await db.PasswordResetTokens.OrderBy(token => token.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Null(tokens[0].RevokedAtUtc);
        Assert.Null(tokens[0].RevocationReason);
        Assert.NotNull(tokens[1].RevokedAtUtc);
        Assert.Equal("DeliveryFailed", tokens[1].RevocationReason);
    }

    [Theory]
    [InlineData("555-123-4567", "+15551234567")]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    public void ContactNormalizer_NormalizesUsPhoneToE164(string input, string expected)
    {
        var result = new ContactNormalizer().NormalizePhone(input);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.NormalizedValue);
    }

    [Fact]
    public async Task NullProviders_AreRejectedInProduction()
    {
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var store = new DevelopmentCommunicationMessageStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NullEmailSender(environment, store, NullLogger<NullEmailSender>.Instance)
                .SendEmailAsync(new EmailMessage
                {
                    ToAddress = "test@example.com",
                    Subject = "Test",
                    PlainTextBody = "Test",
                    Purpose = DeliveryPurpose.PasswordReset
                }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NullSmsSender(environment, store, NullLogger<NullSmsSender>.Instance)
                .SendSmsAsync(new SmsMessage
                {
                    ToNumber = "5550100000",
                    Body = "Test",
                    Purpose = DeliveryPurpose.PasswordReset
                }));
    }

    [Fact]
    public void Templates_DoNotContainCommonPhiLabels()
    {
        var repoRoot = FindRepoRoot();
        var templateDirectory = Path.Combine(
            repoRoot,
            "src",
            "PTDoc.Infrastructure",
            "Communication",
            "Templates");

        foreach (var path in Directory.EnumerateFiles(templateDirectory))
        {
            var content = File.ReadAllText(path);
            Assert.DoesNotContain("diagnosis", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("insurance", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("date of birth", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DOB", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clinical note", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FileTemplateRenderer_RejectsUnresolvedPlaceholders()
    {
        var renderer = new FileMessageTemplateRenderer(NullLogger<FileMessageTemplateRenderer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderAsync("password-reset-email.html", new Dictionary<string, string>()));
    }

    [Fact]
    public void PasswordResetTemplates_UsePinTerminology()
    {
        var repoRoot = FindRepoRoot();
        var templateDirectory = Path.Combine(repoRoot, "src", "PTDoc.Infrastructure", "Communication", "Templates");
        var emailTemplate = File.ReadAllText(Path.Combine(templateDirectory, "password-reset-email.html"));
        var smsTemplate = File.ReadAllText(Path.Combine(templateDirectory, "password-reset-sms.txt"));

        Assert.Contains("Reset your PTDoc PIN", emailTemplate, StringComparison.Ordinal);
        Assert.Contains("reset your PIN", smsTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("password", emailTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", smsTemplate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionStartupValidation_FailsWhenAzureConfigurationIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Communication:PublicBaseUrl"] = "https://app.ptdoc.com",
                ["Communication:RecipientHashSalt"] = "production-salt",
                ["Communication:TokenExpiryMinutes:PasswordReset"] = "30",
                ["Communication:TokenExpiryMinutes:Intake"] = "10080"
            })
            .Build();

        var services = new ServiceCollection();
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPTDocCommunication(configuration, environment));

        Assert.Contains("Communication:Azure:ConnectionString", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionStartupValidation_FailsWhenPublicBaseUrlIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Communication:RecipientHashSalt"] = "production-salt",
                ["Communication:Azure:ConnectionString"] = "endpoint=https://example.communication.azure.com/;accesskey=test",
                ["Communication:Azure:EmailFromAddress"] = "no-reply@ptdoc.com",
                ["Communication:Azure:SmsFromPhoneNumber"] = "+15550100000"
            })
            .Build();

        var services = new ServiceCollection();
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPTDocCommunication(configuration, environment));

        Assert.Contains("Communication:PublicBaseUrl is not configured.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionStartupValidation_FailsWhenPublicBaseUrlIsLoopback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Communication:PublicBaseUrl"] = "http://localhost:5000",
                ["Communication:RecipientHashSalt"] = "production-salt",
                ["Communication:Azure:ConnectionString"] = "endpoint=https://example.communication.azure.com/;accesskey=test",
                ["Communication:Azure:EmailFromAddress"] = "no-reply@ptdoc.com",
                ["Communication:Azure:SmsFromPhoneNumber"] = "+15550100000"
            })
            .Build();

        var services = new ServiceCollection();
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPTDocCommunication(configuration, environment));

        Assert.Contains("Communication:PublicBaseUrl must be an explicit non-loopback URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommunicationRegistration_UsesFallbacksForNonPositiveIntegerConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Communication:TokenExpiryMinutes:PasswordReset"] = "0",
                ["Communication:TokenExpiryMinutes:Intake"] = "-10",
                ["Communication:Retention:ResetTokensDays"] = "0",
                ["Communication:Retention:DeliveryLogsDays"] = "-1",
                ["Communication:RateLimits:PasswordResetMaxPerWindow"] = "0",
                ["Communication:RateLimits:PasswordResetWindowMinutes"] = "-15",
                ["Communication:RateLimits:IntakeMaxPerDay"] = "-5"
            })
            .Build();

        var services = new ServiceCollection();
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };

        services.AddPTDocCommunication(configuration, environment);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CommunicationOptions>>().Value;

        Assert.Equal(30, options.TokenExpiryMinutes.PasswordReset);
        Assert.Equal(10080, options.TokenExpiryMinutes.Intake);
        Assert.Equal(30, options.Retention.ResetTokensDays);
        Assert.Equal(2190, options.Retention.DeliveryLogsDays);
        Assert.Equal(3, options.RateLimits.PasswordResetMaxPerWindow);
        Assert.Equal(15, options.RateLimits.PasswordResetWindowMinutes);
        Assert.Equal(5, options.RateLimits.IntakeMaxPerDay);
    }

    [Fact]
    public async Task NullSenders_CaptureMessagesForDevelopmentDiagnostics()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PTDOC_DEVELOPER_MODE"] = "true"
        });
        var store = new DevelopmentCommunicationMessageStore();
        var environment = new TestHostEnvironment();
        var emailSender = new NullEmailSender(
            environment,
            store,
            NullLogger<NullEmailSender>.Instance);
        var smsSender = new NullSmsSender(
            environment,
            store,
            NullLogger<NullSmsSender>.Instance);

        await emailSender.SendEmailAsync(new EmailMessage
        {
            ToAddress = "patient@example.com",
            Subject = "Your code",
            PlainTextBody = "Your verification code is 123456.",
            HtmlBody = "<p>Your verification code is 123456.</p>",
            Purpose = DeliveryPurpose.IntakeOtp
        });
        await smsSender.SendSmsAsync(new SmsMessage
        {
            ToNumber = "555-0101",
            Body = "Your verification code is 654321.",
            Purpose = DeliveryPurpose.IntakeOtp
        });

        var messages = store.List();

        Assert.Equal(2, messages.Count);
        Assert.Equal(DeliveryChannel.Sms, messages[0].Channel);
        Assert.Equal("555-0101", messages[0].Recipient);
        Assert.Contains("654321", messages[0].PlainTextBody, StringComparison.Ordinal);
        Assert.Equal(DeliveryChannel.Email, messages[1].Channel);
        Assert.Equal("patient@example.com", messages[1].Recipient);
        Assert.Equal("Your code", messages[1].Subject);
        Assert.Contains("123456", messages[1].PlainTextBody, StringComparison.Ordinal);
        Assert.Contains("123456", messages[1].HtmlBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullSenders_DoNotRetainMessages_WhenDeveloperDiagnosticsDisabled()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PTDOC_DEVELOPER_MODE"] = "false"
        });
        var store = new DevelopmentCommunicationMessageStore();
        var environment = new TestHostEnvironment();
        var emailSender = new NullEmailSender(
            environment,
            store,
            NullLogger<NullEmailSender>.Instance);
        var smsSender = new NullSmsSender(
            environment,
            store,
            NullLogger<NullSmsSender>.Instance);

        var emailResult = await emailSender.SendEmailAsync(new EmailMessage
        {
            ToAddress = "patient@example.com",
            Subject = "Your code",
            PlainTextBody = "Your verification code is 123456.",
            HtmlBody = "<p>Your verification code is 123456.</p>",
            Purpose = DeliveryPurpose.IntakeOtp
        });
        var smsResult = await smsSender.SendSmsAsync(new SmsMessage
        {
            ToNumber = "555-0101",
            Body = "Your verification code is 654321.",
            Purpose = DeliveryPurpose.IntakeOtp
        });

        Assert.True(emailResult.Succeeded);
        Assert.True(smsResult.Succeeded);
        Assert.Empty(store.List());
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PTDoc.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static CommunicationService CreateService(
        ApplicationDbContext db,
        IEmailSender? emailSender = null,
        ISmsSender? smsSender = null)
    {
        var options = Options.Create(new CommunicationOptions
        {
            PublicBaseUrl = "http://localhost:5000",
            RecipientHashSalt = "test-recipient-hash-salt",
            TokenExpiryMinutes = new TokenExpiryOptions
            {
                PasswordReset = 30,
                Intake = 10080
            }
        });

        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };
        var contactNormalizer = new ContactNormalizer();
        var auditWriter = new CommunicationAuditWriter(db, options, environment, contactNormalizer);

        return new CommunicationService(
            db,
            emailSender ?? new FakeEmailSender(),
            smsSender ?? new FakeSmsSender(),
            new FakeTemplateRenderer(),
            auditWriter,
            contactNormalizer,
            options,
            NullLogger<CommunicationService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string ExtractResetToken(FakeEmailSender emailSender)
    {
        var link = emailSender.Messages.Single().PlainTextBody.Split(": ", StringSplitOptions.None).Last();
        var token = new Uri(link).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts => parts[0] == "token")?[1];

        Assert.False(string.IsNullOrWhiteSpace(token));
        return Uri.UnescapeDataString(token!);
    }

    private sealed class FakeTemplateRenderer : IMessageTemplateRenderer
    {
        public Task<string> RenderAsync(
            string templateName,
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"{templateName}:{values["Link"]}");
    }

    public sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task<DeliveryResult> SendEmailAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.FromResult(new DeliveryResult
            {
                Succeeded = true,
                Status = DeliveryStatus.Sent,
                Provider = "Fake",
                ProviderMessageId = $"fake-email-{Messages.Count}",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Email,
                Purpose = message.Purpose
            });
        }
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task<DeliveryResult> SendEmailAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new DeliveryResult
            {
                Succeeded = false,
                Status = DeliveryStatus.Failed,
                Provider = "Fake",
                ErrorCode = "FakeFailure",
                SafeErrorMessage = "Delivery failed.",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Email,
                Purpose = message.Purpose
            });
    }

    public sealed class FakeSmsSender : ISmsSender
    {
        public List<SmsMessage> Messages { get; } = [];

        public Task<DeliveryResult> SendSmsAsync(
            SmsMessage message,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.FromResult(new DeliveryResult
            {
                Succeeded = true,
                Status = DeliveryStatus.Sent,
                Provider = "Fake",
                ProviderMessageId = $"fake-sms-{Messages.Count}",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = DeliveryChannel.Sms,
                Purpose = message.Purpose
            });
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                _previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previousValue) in _previousValues)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PTDoc.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
