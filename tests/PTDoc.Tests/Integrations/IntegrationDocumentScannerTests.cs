using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PTDoc.Application.Integrations;
using PTDoc.Infrastructure.Integrations;
using Xunit;

namespace PTDoc.Tests.Integrations;

[Trait("Category", "CoreCi")]
public sealed class IntegrationDocumentScannerTests
{
    [Fact]
    public async Task Development_AllowsValidPdfSignatureWithoutClamAv()
    {
        var scanner = CreateScanner(Environments.Development);
        await using var content = new MemoryStream("%PDF-1.7\nsynthetic-test"u8.ToArray());

        await scanner.ScanAsync(content, "application/pdf");
    }

    [Fact]
    public async Task Development_RejectsMismatchedPdfContent()
    {
        var scanner = CreateScanner(Environments.Development);
        await using var content = new MemoryStream("not-a-pdf"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scanner.ScanAsync(content, "application/pdf"));
    }

    [Fact]
    public async Task Production_FailsClosedWithoutClamAv()
    {
        var scanner = CreateScanner(Environments.Production);
        await using var content = new MemoryStream("%PDF-1.7\nsynthetic-test"u8.ToArray());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scanner.ScanAsync(content, "application/pdf"));

        Assert.Equal("Document malware scanning is not configured.", exception.Message);
    }

    private static ClamAvIntegrationDocumentScanner CreateScanner(string environmentName) =>
        new(
            new IntegrationDocumentScannerOptions { Enabled = true },
            new TestHostEnvironment { EnvironmentName = environmentName });

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "PTDoc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
