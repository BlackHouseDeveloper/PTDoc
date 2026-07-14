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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PTDoc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
