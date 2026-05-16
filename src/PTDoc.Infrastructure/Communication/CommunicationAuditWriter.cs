using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Communication;

public sealed class CommunicationAuditWriter : ICommunicationAuditWriter
{
    private readonly ApplicationDbContext _db;
    private readonly CommunicationOptions _options;
    private readonly IHostEnvironment _environment;

    public CommunicationAuditWriter(
        ApplicationDbContext db,
        IOptions<CommunicationOptions> options,
        IHostEnvironment environment)
    {
        _db = db;
        _options = options.Value;
        _environment = environment;
    }

    public string HashRecipient(string recipient)
    {
        var salt = _options.RecipientHashSalt;
        if (string.IsNullOrWhiteSpace(salt))
        {
            if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException("Communication:RecipientHashSalt must be configured outside Development and Testing.");
            }

            salt = "development-only-recipient-hash-salt";
        }

        var normalized = CommunicationText.NormalizeRecipient(recipient);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task WriteAsync(
        CommunicationAuditWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var log = new CommunicationDeliveryLog
        {
            Id = Guid.NewGuid(),
            PatientId = request.PatientId,
            UserId = request.UserId,
            Purpose = request.Purpose,
            Channel = request.Channel,
            RecipientHash = HashRecipient(request.Recipient),
            Provider = request.Provider,
            ProviderMessageId = request.ProviderMessageId,
            Status = request.Status,
            ErrorCode = request.ErrorCode,
            SafeErrorMessage = request.SafeErrorMessage,
            SentAtUtc = request.SentAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = request.CorrelationId,
            RetryCount = request.RetryCount
        };

        _db.CommunicationDeliveryLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
