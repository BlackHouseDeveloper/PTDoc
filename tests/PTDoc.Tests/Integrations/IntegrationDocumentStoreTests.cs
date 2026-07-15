using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PTDoc.Application.Integrations;
using PTDoc.Infrastructure.Integrations;
using Xunit;

namespace PTDoc.Tests.Integrations;

[Trait("Category", "CoreCi")]
public sealed class IntegrationDocumentStoreTests
{
    [Theory]
    [InlineData("clinic\\document.pdf")]
    [InlineData("C:document.pdf")]
    public async Task OpenReadAsync_RejectsPlatformSpecificStorageKeySeparators(string storageKey)
    {
        var store = new IntegrationDocumentStore(
            new AzureBlobStorageOptions(),
            new IntegrationDocumentStoreOptions { DevelopmentPath = Path.GetTempPath() },
            new TestHostEnvironment { EnvironmentName = "Testing" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenReadAsync(storageKey));

        Assert.Equal("Integration document storage key is invalid.", exception.Message);
    }

    [Fact]
    public async Task SaveAsync_ComputesHashWhileWritingTheDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ptdoc-integration-documents-{Guid.NewGuid():N}");
        try
        {
            var store = new IntegrationDocumentStore(
                new AzureBlobStorageOptions(),
                new IntegrationDocumentStoreOptions { DevelopmentPath = root },
                new TestHostEnvironment { EnvironmentName = "Testing" });
            var content = "integration document content"u8.ToArray();

            await using var stream = new MemoryStream(content);
            var stored = await store.SaveAsync(Guid.NewGuid(), "inbound-fax", "document.pdf", "application/pdf", stream);

            Assert.Equal(content.Length, stored.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), stored.HashSha256);
            await using var saved = await store.OpenReadAsync(stored.StorageKey);
            using var savedContent = new MemoryStream();
            await saved.CopyToAsync(savedContent);
            Assert.Equal(content, savedContent.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PTDoc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
