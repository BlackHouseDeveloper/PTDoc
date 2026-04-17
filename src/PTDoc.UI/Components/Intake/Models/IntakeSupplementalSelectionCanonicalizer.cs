using PTDoc.Application.ReferenceData;

namespace PTDoc.UI.Components.Intake.Models;

public static class IntakeSupplementalSelectionCanonicalizer
{
    public static void Canonicalize(
        IntakeWizardState state,
        IIntakeReferenceDataCatalogService catalogService)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogService);

        var structuredData = state.EnsureStructuredData(catalogService.GetCatalog().Version);

        CanonicalizeSelections(
            structuredData.ComorbidityIds,
            state.SelectedComorbidities,
            catalogService.GetComorbidities());
        CanonicalizeSelections(
            structuredData.AssistiveDeviceIds,
            state.SelectedAssistiveDevices,
            catalogService.GetAssistiveDevices());
        CanonicalizeSelections(
            structuredData.LivingSituationIds,
            state.SelectedLivingSituations,
            catalogService.GetLivingSituations());
        CanonicalizeSelections(
            structuredData.HouseLayoutOptionIds,
            state.SelectedHouseLayoutOptions,
            catalogService.GetHouseLayoutOptions());

        state.HasOtherMedicalConditions = structuredData.ComorbidityIds.Count > 0 || state.SelectedComorbidities.Count > 0;
        state.UsesAssistiveDevices = structuredData.AssistiveDeviceIds.Count > 0 || state.SelectedAssistiveDevices.Count > 0;
    }

    private static void CanonicalizeSelections(
        List<string> structuredIds,
        HashSet<string> legacySelections,
        IReadOnlyList<IntakeCatalogOptionDto> catalog)
    {
        if (structuredIds.Count > 0)
        {
            legacySelections.Clear();
            return;
        }

        if (legacySelections.Count == 0)
        {
            return;
        }

        var unmatchedSelections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawSelection in legacySelections)
        {
            var selection = rawSelection?.Trim();
            if (string.IsNullOrWhiteSpace(selection))
            {
                continue;
            }

            var match = catalog.FirstOrDefault(option =>
                string.Equals(option.Id, selection, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Label, selection, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unmatchedSelections.Add(selection);
                continue;
            }

            if (!structuredIds.Contains(match.Id, StringComparer.OrdinalIgnoreCase))
            {
                structuredIds.Add(match.Id);
            }
        }

        legacySelections.Clear();

        foreach (var unmatchedSelection in unmatchedSelections)
        {
            legacySelections.Add(unmatchedSelection);
        }
    }
}
