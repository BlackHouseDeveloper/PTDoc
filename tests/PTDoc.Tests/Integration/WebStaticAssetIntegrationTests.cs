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
    private static readonly object EnvironmentLock = new();

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

    private sealed class PTDocWebFactory : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        private readonly string environmentName;
        private readonly string webContentRoot;
        private readonly string? publishedWebRoot;

        public PTDocWebFactory(string environmentName)
        {
            this.environmentName = environmentName;
            webContentRoot = ResolveWebContentRoot();

            if (string.Equals(environmentName, BetaEnvironmentName, StringComparison.Ordinal))
            {
                publishedWebRoot = CreatePublishedWebRoot(webContentRoot);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.UseContentRoot(webContentRoot);

            if (publishedWebRoot is not null)
            {
                builder.UseWebRoot(publishedWebRoot);
            }
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            if (!string.Equals(environmentName, BetaEnvironmentName, StringComparison.Ordinal))
            {
                return base.CreateHost(builder);
            }

            lock (EnvironmentLock)
            {
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
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                if (publishedWebRoot is not null && Directory.Exists(publishedWebRoot))
                {
                    Directory.Delete(publishedWebRoot, recursive: true);
                }
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

        private static string CreatePublishedWebRoot(string webContentRoot)
        {
            var repositoryRoot = ResolveRepositoryRoot(webContentRoot);
            var root = Path.Combine(Path.GetTempPath(), $"ptdoc-web-assets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            CopyFile(
                Path.Combine(repositoryRoot, "src", "PTDoc.UI", "wwwroot", "css", "app.css"),
                Path.Combine(root, "_content", "PTDoc.UI", "css", "app.css"));
            CopyFile(
                Path.Combine(repositoryRoot, "src", "PTDoc.UI", "wwwroot", "js", "theme.js"),
                Path.Combine(root, "_content", "PTDoc.UI", "js", "theme.js"));
            CopyFile(
                Path.Combine(repositoryRoot, "src", "PTDoc.UI", "wwwroot", "ptdoclogo.png"),
                Path.Combine(root, "_content", "PTDoc.UI", "ptdoclogo.png"));
            CopyFirstExisting(
                [
                    Path.Combine(repositoryRoot, "src", "PTDoc.UI", "obj", "Debug", "net8.0", "scopedcss", "projectbundle", "PTDoc.UI.bundle.scp.css"),
                    Path.Combine(repositoryRoot, "src", "PTDoc.UI", "obj", "Release", "net8.0", "scopedcss", "projectbundle", "PTDoc.UI.bundle.scp.css")
                ],
                Path.Combine(root, "_content", "PTDoc.UI", "PTDoc.UI.bundle.scp.css"));
            CopyFirstExisting(
                [
                    Path.Combine(repositoryRoot, "src", "PTDoc.Web", "obj", "Debug", "net8.0", "scopedcss", "bundle", "PTDoc.Web.styles.css"),
                    Path.Combine(repositoryRoot, "src", "PTDoc.Web", "obj", "Release", "net8.0", "scopedcss", "bundle", "PTDoc.Web.styles.css")
                ],
                Path.Combine(root, "PTDoc.Web.styles.css"));

            return root;
        }

        private static string ResolveRepositoryRoot(string webContentRoot)
        {
            var directory = new DirectoryInfo(webContentRoot);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "PTDoc.Web", "PTDoc.Web.csproj"))
                    && File.Exists(Path.Combine(directory.FullName, "src", "PTDoc.UI", "PTDoc.UI.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root for static asset integration tests.");
        }

        private static void CopyFirstExisting(IReadOnlyList<string> sourceCandidates, string destination)
        {
            foreach (var source in sourceCandidates)
            {
                if (File.Exists(source))
                {
                    CopyFile(source, destination);
                    return;
                }
            }

            throw new InvalidOperationException(
                "Could not locate generated scoped CSS asset. Build the test project before running static asset integration tests.");
        }

        private static void CopyFile(string source, string destination)
        {
            if (!File.Exists(source))
            {
                throw new InvalidOperationException($"Could not locate required static asset '{source}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }
}
