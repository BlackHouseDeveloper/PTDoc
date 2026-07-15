using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using PTDoc.Application.Integrations;

namespace PTDoc.Infrastructure.Integrations;

/// <summary>
/// Streams integration documents through a ClamAV INSTREAM endpoint. Hosted
/// environments fail closed when a scanner is not configured; local development
/// still performs MIME/signature and size validation without requiring ClamAV.
/// </summary>
public sealed class ClamAvIntegrationDocumentScanner : IIntegrationDocumentScanner
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] InStreamCommand = "INSTREAM\0"u8.ToArray();
    private readonly IntegrationDocumentScannerOptions _options;
    private readonly bool _allowSignatureOnly;

    public ClamAvIntegrationDocumentScanner(
        IntegrationDocumentScannerOptions options,
        IHostEnvironment environment)
    {
        _options = options;
        _allowSignatureOnly = environment.IsDevelopment() || environment.IsEnvironment("Testing");
    }

    public async Task ScanAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Integration documents must be PDF files.");
        }

        var maxBytes = Math.Clamp(_options.MaxFileBytes, 1, 50 * 1024 * 1024);
        var useClamAv = _options.Enabled && !string.IsNullOrWhiteSpace(_options.Host);
        if (!useClamAv && !_allowSignatureOnly)
        {
            throw new InvalidOperationException("Document malware scanning is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(90) : _options.Timeout);
        using var client = useClamAv ? new TcpClient() : null;
        NetworkStream? network = null;
        if (client is not null)
        {
            await client.ConnectAsync(_options.Host, Math.Clamp(_options.Port, 1, 65535), timeout.Token);
            network = client.GetStream();
            await network.WriteAsync(InStreamCommand, timeout.Token);
        }

        var buffer = new byte[64 * 1024];
        var lengthPrefix = new byte[4];
        var signature = new byte[PdfSignature.Length];
        var signatureLength = 0;
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(), timeout.Token);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Integration document exceeds the configured size limit.");
            }
            if (signatureLength < signature.Length)
            {
                var copy = Math.Min(read, signature.Length - signatureLength);
                buffer.AsSpan(0, copy).CopyTo(signature.AsSpan(signatureLength));
                signatureLength += copy;
            }
            if (network is not null)
            {
                BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, read);
                await network.WriteAsync(lengthPrefix, timeout.Token);
                await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
        }

        if (total == 0 || signatureLength != PdfSignature.Length || !signature.AsSpan().SequenceEqual(PdfSignature))
        {
            throw new InvalidOperationException("Integration document content does not match PDF format.");
        }
        if (network is null)
        {
            return;
        }

        Array.Clear(lengthPrefix);
        await network.WriteAsync(lengthPrefix, timeout.Token);
        await network.FlushAsync(timeout.Token);
        var responseBuffer = new byte[4096];
        var responseLength = 0;
        while (responseLength < responseBuffer.Length)
        {
            var read = await network.ReadAsync(responseBuffer.AsMemory(responseLength), timeout.Token);
            if (read == 0)
            {
                break;
            }
            responseLength += read;
            if (responseBuffer.AsSpan(0, responseLength).Contains((byte)0))
            {
                break;
            }
        }
        var response = Encoding.UTF8.GetString(responseBuffer, 0, responseLength).TrimEnd('\0', '\r', '\n');
        if (response.EndsWith(" OK", StringComparison.Ordinal))
        {
            return;
        }
        if (response.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Integration document was rejected by malware scanning.");
        }
        throw new InvalidOperationException("Document malware scanner returned an invalid result.");
    }
}
