using System.Buffers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
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
                using var hashing = new HashingReadStream(content);
                await using var limited = new LimitedReadStream(hashing, _maxFileBytes);
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
                var hash = hashing.GetHash();
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
            using var hashing = new HashingReadStream(content);
            await using (var destination = new FileStream(
                             fullPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithLimitAsync(hashing, destination, _maxFileBytes, cancellationToken);
            }

            var info = new FileInfo(fullPath);
            return new StoredIntegrationDocument(storageKey, safeFileName, contentType, info.Length, hashing.GetHash());
        }
        catch
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            throw;
        }

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
        var normalizedRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        normalizedRoot = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(normalizedRoot, pathComparison))
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

    private sealed class HashingReadStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Track(buffer, offset, read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out ArraySegment<byte> segment))
            {
                Track(segment.Array!, segment.Offset, read);
            }
            else if (read > 0)
            {
                var rented = ArrayPool<byte>.Shared.Rent(read);
                try
                {
                    buffer[..read].CopyTo(rented.AsMemory(0, read));
                    Track(rented, 0, read);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented, clearArray: true);
                }
            }
            else
            {
                Track(Array.Empty<byte>(), 0, 0);
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }
        }

        public string GetHash()
        {
            if (!_completed)
            {
                throw new InvalidOperationException("Integration document hash was requested before upload completed.");
            }

            return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        }

        private void Track(byte[] buffer, int offset, int count)
        {
            if (count == 0)
            {
                _completed = true;
                return;
            }

            if (_completed)
            {
                throw new InvalidOperationException("Integration document stream was read after completion.");
            }

            _hash.AppendData(buffer, offset, count);
        }
    }
}
