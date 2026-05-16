using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class FileMessageTemplateRenderer : IMessageTemplateRenderer
{
    private static readonly Regex UnresolvedPlaceholder = new(@"\{\{[A-Za-z0-9_.-]+\}\}", RegexOptions.Compiled);

    private readonly ILogger<FileMessageTemplateRenderer> _logger;

    public FileMessageTemplateRenderer(ILogger<FileMessageTemplateRenderer> logger)
    {
        _logger = logger;
    }

    public async Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("Template name is required.", nameof(templateName));
        }

        var path = ResolveTemplatePath(templateName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Communication template '{templateName}' was not found.", path);
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var isHtmlTemplate = templateName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            content = content.Replace(
                "{{" + pair.Key + "}}",
                isHtmlTemplate
                    ? CommunicationText.HtmlEncode(pair.Value ?? string.Empty)
                    : pair.Value ?? string.Empty,
                StringComparison.Ordinal);
        }

        var unresolved = UnresolvedPlaceholder.Match(content);
        if (unresolved.Success)
        {
            throw new InvalidOperationException(
                $"Communication template '{templateName}' contains unresolved placeholder '{unresolved.Value}'.");
        }

        _logger.LogDebug("Rendered communication template {TemplateName}", templateName);
        return content;
    }

    private static string ResolveTemplatePath(string templateName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var outputPath = Path.Combine(baseDirectory, "Communication", "Templates", templateName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var sourcePath = Path.Combine(
                directory.FullName,
                "src",
                "PTDoc.Infrastructure",
                "Communication",
                "Templates",
                templateName);

            if (File.Exists(sourcePath))
            {
                return sourcePath;
            }

            directory = directory.Parent;
        }

        return outputPath;
    }
}
