using System.Reflection;
using System.Xml.Linq;
using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Intake.Steps;

internal static class BodySvgPathProvider
{
    private const string FrontResourceName = "PTDoc.UI.Assets.molemapper.ptdoc.clinical.front.svg";
    private const string BackResourceName = "PTDoc.UI.Assets.molemapper.ptdoc.clinical.back.svg";

    private static readonly Lazy<IReadOnlyDictionary<BodyRegion, string>> FrontPaths =
        new(() => LoadPaths(FrontResourceName, BodyRegionMap.FrontRegions));

    private static readonly Lazy<IReadOnlyDictionary<BodyRegion, string>> BackPaths =
        new(() => LoadPaths(BackResourceName, BodyRegionMap.BackRegions));

    public static IReadOnlyDictionary<BodyRegion, string> GetPathsForView(BodyView view)
    {
        return view == BodyView.Front ? FrontPaths.Value : BackPaths.Value;
    }

    private static IReadOnlyDictionary<BodyRegion, string> LoadPaths(
        string resourceName,
        IEnumerable<BodyRegion> regions)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded SVG resource '{resourceName}' was not found.");
        }

        var document = XDocument.Load(stream);
        var svgNamespace = document.Root?.Name.Namespace ?? XNamespace.None;

        var pathById = document
            .Descendants(svgNamespace + "path")
            .Select(path => new
            {
                Id = path.Attribute("id")?.Value,
                Data = path.Attribute("d")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Data))
            .ToDictionary(item => item.Id!, item => item.Data!, StringComparer.Ordinal);

        var result = new Dictionary<BodyRegion, string>();

        foreach (var region in regions)
        {
            if (!BodyRegionMap.SvgRegionIds.TryGetValue(region, out var regionId))
            {
                continue;
            }

            if (pathById.TryGetValue(regionId, out var pathData))
            {
                result[region] = pathData;
            }
        }

        return result;
    }
}
