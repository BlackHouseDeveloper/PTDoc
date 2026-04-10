using PTDoc.Application.Data;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Adapts the canonical workspace lookup catalog to the legacy generic ICD endpoint contract.
/// This keeps the generic endpoint and workspace lookup on one runtime dataset during Branch 2.
/// </summary>
public sealed class WorkspaceCatalogIcd10Service(IWorkspaceReferenceCatalogService catalogs) : IIcd10Service
{
    public IReadOnlyList<Icd10Code> Search(string query, int maxResults = 20)
        => string.IsNullOrWhiteSpace(query)
            ? Array.Empty<Icd10Code>()
            : catalogs.SearchIcd10(query, maxResults)
                .Select(Map)
                .ToList()
                .AsReadOnly();

    public Icd10Code? GetByCode(string code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : catalogs.SearchIcd10(code.Trim(), take: 100)
                .Where(entry => string.Equals(entry.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(Map)
                .FirstOrDefault();

    private static Icd10Code Map(CodeLookupEntry entry)
        => new()
        {
            Code = entry.Code,
            Description = entry.Description
        };
}
