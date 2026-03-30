using System.Reflection;
using System.Text.Json;
using PTDoc.Application.Data;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// ICD-10 lookup service backed by the bundled Icd10Codes.json resource.
/// Loaded once at first call and cached for the application lifetime.
/// </summary>
public sealed class BundledIcd10Service : IIcd10Service
{
    private readonly Lazy<IReadOnlyList<Icd10Code>> _codes = new(LoadCodes, LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<Icd10Code> Search(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<Icd10Code>();

        var normalized = query.Trim();

        return _codes.Value
            .Where(c =>
                c.Code.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList()
            .AsReadOnly();
    }

    public Icd10Code? GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var normalized = code.Trim();
        return _codes.Value.FirstOrDefault(
            c => string.Equals(c.Code, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Icd10Code> LoadCodes()
    {
        // Try loading from PTDoc.Application assembly using the well-known resource name
        const string resourceSuffix = "PTDoc.Application.Data.Icd10Codes.json";

        var appAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "PTDoc.Application");

        if (appAssembly is not null)
        {
            using var stream = appAssembly.GetManifestResourceStream(resourceSuffix);
            if (stream is not null)
                return ParseCodes(stream);
        }

        // Fallback: scan all loaded assemblies for the resource by suffix
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Icd10Codes.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                continue;

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is not null)
                return ParseCodes(stream);
        }

        return Array.Empty<Icd10Code>();
    }

    private static IReadOnlyList<Icd10Code> ParseCodes(Stream stream)
    {
        var codes = JsonSerializer.Deserialize<List<Icd10Code>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return codes?.AsReadOnly() ?? (IReadOnlyList<Icd10Code>)Array.Empty<Icd10Code>();
    }
}
