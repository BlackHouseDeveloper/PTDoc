using System.Reflection;
using System.Text.Json;
using PTDoc.Application.ReferenceData;

namespace PTDoc.Infrastructure.ReferenceData;

internal static class EmbeddedJsonResourceLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static T LoadFromApplicationAssembly<T>(string resourceName)
        where T : class, new()
    {
        var appAssembly = typeof(IIntakeReferenceDataCatalogService).Assembly;

        using var stream = TryOpenResourceStream(appAssembly, resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found in PTDoc.Application.");
        }

        return JsonSerializer.Deserialize<T>(stream, SerializerOptions) ?? new T();
    }

    private static Stream? TryOpenResourceStream(Assembly assembly, string resourceName)
    {
        var direct = assembly.GetManifestResourceStream(resourceName);
        if (direct is not null)
        {
            return direct;
        }

        var fileName = resourceName.Split('.').Length >= 2
            ? string.Join('.', resourceName.Split('.').TakeLast(2))
            : resourceName;

        var matched = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        return matched is null ? null : assembly.GetManifestResourceStream(matched);
    }
}
