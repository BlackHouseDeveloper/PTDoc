using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class WebStaticAssetIntegrationTests
{
    private const string BetaEnvironmentName = "Beta";
    private const string EntraClientSecretEnvironmentVariable = "EntraExternalId__ClientSecret";
    private const string TestEntraClientSecret = "web-static-asset-test-client-secret-placeholder";

    public static TheoryData<string, string, string> ServedAssets =>
        new()
        {
            { "Development", "/_content/PTDoc.UI/css/app.css", "text/css" },
            { "Development", "/_content/PTDoc.UI/PTDoc.UI.bundle.scp.css", "text/css" },
            { "Development", "/PTDoc.Web.styles.css", "text/css" },
            { "Development", "/js/auth.js", "javascript" },
            { "Development", "/_content/PTDoc.UI/js/modal.js", "javascript" },
            { "Development", "/_content/PTDoc.UI/images/nav-home.svg", "image/svg+xml" },
            { "Beta", "/_content/PTDoc.UI/css/app.css", "text/css" },
            { "Beta", "/_content/PTDoc.UI/PTDoc.UI.bundle.scp.css", "text/css" },
            { "Beta", "/PTDoc.Web.styles.css", "text/css" },
            { "Beta", "/_content/PTDoc.UI/js/theme.js", "javascript" },
            { "Beta", "/_content/PTDoc.UI/ptdoclogo.png", "image/png" }
        };

    [Theory]
    [MemberData(nameof(ServedAssets))]
    public async Task StaticAssets_AreServedWithoutAuthRedirect(string environmentName, string path, string expectedContentType)
    {
        await using var factory = new PTDocWebFactory(environmentName);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.False(string.IsNullOrWhiteSpace(contentType));
        Assert.Contains(expectedContentType, contentType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingStaticAssets_ReturnNotFoundInsteadOfLoginRedirect()
    {
        await using var factory = new PTDocWebFactory("Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/_content/PTDoc.UI/js/not-real.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/unknown-route-with-extension.pdf")]
    [InlineData("/_content/Other.Package/not-real.js")]
    public async Task UnknownExtensionRoutes_AreNotShortCircuitedAsStaticAssets(string path)
    {
        await using var factory = new PTDocWebFactory("Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Page not found", body, StringComparison.Ordinal);
    }

    private sealed class PTDocWebFactory(string environmentName)
        : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.UseContentRoot(ResolveWebContentRoot());
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            if (!string.Equals(environmentName, BetaEnvironmentName, StringComparison.Ordinal))
            {
                return base.CreateHost(builder);
            }

            var previousClientSecret = Environment.GetEnvironmentVariable(EntraClientSecretEnvironmentVariable);
            Environment.SetEnvironmentVariable(EntraClientSecretEnvironmentVariable, TestEntraClientSecret);

            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EntraClientSecretEnvironmentVariable, previousClientSecret);
            }
        }

        private static string ResolveWebContentRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var webContentRoot = Path.Combine(directory.FullName, "src", "PTDoc.Web");
                if (File.Exists(Path.Combine(webContentRoot, "PTDoc.Web.csproj")))
                {
                    return webContentRoot;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate src/PTDoc.Web for static asset integration tests.");
        }
    }
}
