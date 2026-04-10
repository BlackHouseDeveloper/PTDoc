using System.Reflection;
using System.Text.Json;

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
        var appAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "PTDoc.Application")
            ?? Assembly.Load("PTDoc.Application");

        using var stream = appAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found in PTDoc.Application.");
        }

        return JsonSerializer.Deserialize<T>(stream, SerializerOptions) ?? new T();
    }
}
