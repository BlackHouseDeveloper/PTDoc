using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using PTDoc.Application.Integrations;

namespace PTDoc.Infrastructure.Integrations;

/// <summary>
/// Private blob-backed integration document store. A local private directory is available
/// only for Development/Testing so local workflows do not require Azure storage.
/// </summary>
public sealed class IntegrationDocumentStore : IIntegrationDocumentStore
{
    private readonly BlobContainerClient? _container;
    private readonly string? _developmentRoot;
    private readonly int _maxFileBytes;

    public IntegrationDocumentStore(
        AzureBlobStorageOptions blobOptions,
        IntegrationDocumentStoreOptions options,
        IHostEnvironment environment)
    {
        _maxFileBytes = Math.Clamp(options.MaxFileBytes, 1, 50 * 1024 * 1024);
        if (!string.IsNullOrWhiteSpace(blobOptions.ConnectionString))
        {
            var client = new BlobServiceClient(blobOptions.ConnectionString);
            _container = client.GetBlobContainerClient(options.ContainerName);
            return;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return;
        }

        var configuredDevelopmentPath = string.IsNullOrWhiteSpace(options.DevelopmentPath)
            ? Path.Combine(AppContext.BaseDirectory, ".ptdoc-integration-documents")
            : options.DevelopmentPath;
        _developmentRoot = Path.GetFullPath(configuredDevelopmentPath);
    }

    public async Task<StoredIntegrationDocument> SaveAsync(
        Guid clinicId,
        string category,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var safeCategory = SanitizeSegment(category, "document");
        var safeFileName = SanitizeFileName(fileName);
        var storageKey = $"{clinicId:D}/{safeCategory}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}-{safeFileName}";

        if (_container is not null)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            var blob = _container.GetBlobClient(storageKey);
            try
            {
                await using var limited = new LimitedReadStream(content, _maxFileBytes);
                await blob.UploadAsync(
                    limited,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                        Metadata = new Dictionary<string, string>
                        {
                            ["clinicId"] = clinicId.ToString("D"),
                            ["category"] = safeCategory
                        }
                    },
                    cancellationToken);

                var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
                await using var hashStream = (await blob.DownloadStreamingAsync(cancellationToken: cancellationToken)).Value.Content;
                var hash = await ComputeHashAsync(hashStream, cancellationToken);
                return new StoredIntegrationDocument(storageKey, safeFileName, contentType, properties.Value.ContentLength, hash);
            }
            catch
            {
                await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                throw;
            }
        }

        var root = _developmentRoot
            ?? throw new InvalidOperationException("Private integration document storage is not configured.");
        var fullPath = GetSafeLocalPath(root, storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            await using var destination = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyWithLimitAsync(content, destination, _maxFileBytes, cancellationToken);
        }
        catch
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            throw;
        }

        var info = new FileInfo(fullPath);
        await using var localHashStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var localHash = await ComputeHashAsync(localHashStream, cancellationToken);
        return new StoredIntegrationDocument(storageKey, safeFileName, contentType, info.Length, localHash);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);
        if (_container is not null)
        {
            var result = await _container.GetBlobClient(storageKey).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return result.Value.Content;
        }

        var root = _developmentRoot
            ?? throw new InvalidOperationException("Private integration document storage is not configured.");
        var fullPath = GetSafeLocalPath(root, storageKey);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);
        if (_container is not null)
        {
            await _container.GetBlobClient(storageKey).DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return;
        }

        var root = _developmentRoot
            ?? throw new InvalidOperationException("Private integration document storage is not configured.");
        var fullPath = GetSafeLocalPath(root, storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Integration document exceeds the configured size limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(value) ? "document.pdf" : value.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }
        return fileName.Length > 200 ? fileName[^200..] : fileName;
    }

    private static string SanitizeSegment(string value, string fallback)
    {
        var segment = new string((value ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(segment) ? fallback : segment[..Math.Min(segment.Length, 80)];
    }

    private static void ValidateStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 1024 ||
            storageKey.Contains("..", StringComparison.Ordinal) || storageKey.StartsWith('/') ||
            storageKey.Contains('\\') || storageKey.Contains(':'))
        {
            throw new InvalidOperationException("Integration document storage key is invalid.");
        }
    }

    private static string GetSafeLocalPath(string root, string storageKey)
    {
        ValidateStorageKey(storageKey);
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Integration document storage key escaped its root.");
        }
        return path;
    }

    private sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Track(await inner.ReadAsync(buffer, cancellationToken));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private int Track(int count)
        {
            _read += count;
            if (_read > maxBytes)
            {
                throw new InvalidOperationException("Integration document exceeds the configured size limit.");
            }
            return count;
        }
    }
}
