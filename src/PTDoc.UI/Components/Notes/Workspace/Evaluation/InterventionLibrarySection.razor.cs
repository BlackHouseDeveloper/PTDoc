using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PTDoc.Application.Notes.Workspace;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;

namespace PTDoc.UI.Components.Notes.Workspace.Evaluation;

public partial class InterventionLibrarySection
{
    private static readonly IReadOnlyList<RegionFilter> Regions =
    [
        new(null, "All"),
        new(InterventionRegion.Shoulder, "Shoulder"),
        new(InterventionRegion.Elbow, "Elbow"),
        new(InterventionRegion.HandWrist, "Hand/Wrist"),
        new(InterventionRegion.CervicalSpine, "Cervical Spine"),
        new(InterventionRegion.LumbarSpine, "Lumbar Spine"),
        new(InterventionRegion.Hip, "Hip"),
        new(InterventionRegion.Knee, "Knee"),
        new(InterventionRegion.AnkleFoot, "Ankle/Foot"),
        new(InterventionRegion.General, "General")
    ];

    private readonly HashSet<ExerciseRowEntry> _collapsedExercises = new();
    private readonly Dictionary<ExerciseRowEntry, Dictionary<string, string>> _exerciseErrors = new();
    private readonly string _modalId = $"intervention-library-dialog-{Guid.NewGuid():N}";
    private InterventionLibraryCatalog? _catalog;
    private InterventionDialogKind _dialogKind;
    private DialogMode _exerciseMode = DialogMode.Library;
    private DialogMode _techniqueMode = DialogMode.Library;
    private string _exerciseSearchQuery = string.Empty;
    private InterventionRegion? _exerciseRegion;
    private InterventionRegion? _techniqueRegion;
    private string _customExerciseName = string.Empty;
    private string _customExerciseNotes = string.Empty;
    private readonly Dictionary<string, string> _customExerciseErrors = new(StringComparer.Ordinal);
    private bool _isCatalogLoading;
    private string? _catalogError;
    private IJSObjectReference? _modalModule;
    private DotNetObjectReference<InterventionLibrarySection>? _dotNetReference;
    private bool _modalAccessibilityActive;
    private ElementReference _exerciseLibraryTab;
    private ElementReference _exerciseCustomTab;
    private ElementReference _techniqueLibraryTab;
    private ElementReference _techniqueCustomTab;

    [Inject] private INoteWorkspaceService NoteWorkspaceService { get; set; } = default!;
    [Inject] private ILogger<InterventionLibrarySection> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public ObjectiveVm Objective { get; set; } = new();
    [Parameter] public EventCallback<ObjectiveVm> ObjectiveChanged { get; set; }
    [Parameter, EditorRequired] public PlanVm Plan { get; set; } = new();
    [Parameter] public EventCallback<PlanVm> PlanChanged { get; set; }
    [Parameter] public bool IsReadOnly { get; set; }
    [Parameter] public string? SelectedBodyPart { get; set; }

    private IReadOnlyList<GeneralInterventionEntry> ManualTechniques => Plan.GeneralInterventions
        .Where(IsManualTechnique)
        .ToList();

    private IReadOnlyList<ExerciseRowEntry> NamedExercises => Objective.ExerciseRows
        .Where(row => !string.IsNullOrWhiteSpace(row.ActualExercisePerformed) ||
                      !string.IsNullOrWhiteSpace(row.SuggestedExercise))
        .ToList();

    private string DialogTitle => _dialogKind == InterventionDialogKind.Exercise
        ? "Add Therapeutic Exercise"
        : "Add Manual Technique";

    private string DialogDescription => _dialogKind == InterventionDialogKind.Exercise
        ? "Select from library or add custom exercise"
        : "Select from library or add custom technique";

    private IEnumerable<InterventionLibraryItem> FilteredExercises => (_catalog?.Items ?? [])
        .Where(item => item.Kind == InterventionKind.Exercise)
        .Where(item => !_exerciseRegion.HasValue || item.Region == _exerciseRegion.Value)
        .Where(item => MatchesExerciseSearch(item, _exerciseSearchQuery));

    private IEnumerable<InterventionLibraryItem> FilteredTechniques => (_catalog?.Items ?? [])
        .Where(item => item.Kind == InterventionKind.ManualTechnique)
        .Where(item => !_techniqueRegion.HasValue || item.Region == _techniqueRegion.Value);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_dialogKind != InterventionDialogKind.None && !_modalAccessibilityActive)
        {
            try
            {
                _modalModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/PTDoc.UI/js/modal.js");
                _dotNetReference ??= DotNetObjectReference.Create(this);
                await _modalModule.InvokeVoidAsync("lockBodyScroll");
                _modalAccessibilityActive = true;
                await _modalModule.InvokeVoidAsync("registerEscapeHandler", _modalId, _dotNetReference);
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (InvalidOperationException) { }
        }
    }

    private async Task OpenExerciseDialogAsync()
    {
        await CleanupModalAsync();
        _dialogKind = InterventionDialogKind.Exercise;
        _exerciseMode = DialogMode.Library;
        _exerciseSearchQuery = string.Empty;
        _exerciseRegion = null;
        await EnsureCatalogAsync();
    }

    private async Task OpenTechniqueDialogAsync()
    {
        await CleanupModalAsync();
        _dialogKind = InterventionDialogKind.Technique;
        _techniqueMode = DialogMode.Library;
        _techniqueRegion = null;
        await EnsureCatalogAsync();
    }

    private async Task EnsureCatalogAsync(bool retry = false)
    {
        if (_catalog is not null && !retry)
        {
            return;
        }

        _isCatalogLoading = true;
        _catalogError = null;
        try
        {
            _catalog = await NoteWorkspaceService.GetInterventionLibraryCatalogAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to load the intervention library catalog");
            _catalog = null;
            _catalogError = "We couldn’t load the intervention library. Try again.";
        }
        finally
        {
            _isCatalogLoading = false;
        }
    }

    private async Task SetExerciseModeAsync(DialogMode mode)
    {
        _exerciseMode = mode;
        await (mode == DialogMode.Library ? _exerciseLibraryTab.FocusAsync() : _exerciseCustomTab.FocusAsync());
    }

    private async Task SetTechniqueModeAsync(DialogMode mode)
    {
        _techniqueMode = mode;
        await (mode == DialogMode.Library ? _techniqueLibraryTab.FocusAsync() : _techniqueCustomTab.FocusAsync());
    }

    private Task HandleExerciseTabKeyAsync(KeyboardEventArgs args) => args.Key switch
    {
        "Home" => SetExerciseModeAsync(DialogMode.Library),
        "End" => SetExerciseModeAsync(DialogMode.Custom),
        "ArrowLeft" or "ArrowRight" => SetExerciseModeAsync(_exerciseMode == DialogMode.Library ? DialogMode.Custom : DialogMode.Library),
        _ => Task.CompletedTask
    };

    private Task HandleTechniqueTabKeyAsync(KeyboardEventArgs args) => args.Key switch
    {
        "Home" => SetTechniqueModeAsync(DialogMode.Library),
        "End" => SetTechniqueModeAsync(DialogMode.Custom),
        "ArrowLeft" or "ArrowRight" => SetTechniqueModeAsync(_techniqueMode == DialogMode.Library ? DialogMode.Custom : DialogMode.Library),
        _ => Task.CompletedTask
    };

    private async Task AddLibraryExerciseAsync(InterventionLibraryItem item)
    {
        Objective.ExerciseRows.Add(new ExerciseRowEntry
        {
            SourceItemId = item.Id,
            SourceCatalogVersion = _catalog?.Version,
            Category = item.Category,
            InterventionRegion = item.Region,
            SuggestedExercise = item.Name,
            ActualExercisePerformed = item.Name,
            IsCheckedSuggestedExercise = true,
            IsSourceBacked = true,
            Prescription = item.DefaultPrescription is null
                ? new ExercisePrescriptionEntry()
                : new ExercisePrescriptionEntry
                {
                    Sets = item.DefaultPrescription.Sets,
                    Repetitions = item.DefaultPrescription.Repetitions,
                    Frequency = item.DefaultPrescription.Frequency
                },
            IncludeInHomeExerciseProgram = false
        });
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task AddCustomExerciseAsync()
    {
        _customExerciseErrors.Clear();
        var name = _customExerciseName.Trim();
        var notes = _customExerciseNotes.Trim();
        if (name.Length == 0) _customExerciseErrors[nameof(_customExerciseName)] = "Exercise name is required.";
        else if (name.Length > 200) _customExerciseErrors[nameof(_customExerciseName)] = "Exercise name cannot exceed 200 characters.";
        if (notes.Length > 2000) _customExerciseErrors[nameof(_customExerciseNotes)] = "Notes cannot exceed 2000 characters.";
        if (_customExerciseErrors.Count > 0) return;

        Objective.ExerciseRows.Add(new ExerciseRowEntry
        {
            SuggestedExercise = name,
            ActualExercisePerformed = name,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            InterventionRegion = ResolveSelectedRegion(),
            IsCheckedSuggestedExercise = true,
            Prescription = new ExercisePrescriptionEntry(),
            IncludeInHomeExerciseProgram = false
        });
        _customExerciseName = string.Empty;
        _customExerciseNotes = string.Empty;
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task AddLibraryTechniqueAsync(InterventionLibraryItem item)
    {
        Plan.GeneralInterventions.Add(new GeneralInterventionEntry
        {
            Kind = InterventionKind.ManualTechnique,
            SourceItemId = item.Id,
            SourceCatalogVersion = _catalog?.Version,
            Name = item.Name,
            Category = item.Category,
            InterventionRegion = item.Region,
            IsSourceBacked = true,
            IncludeInHomeExerciseProgram = false
        });
        await PlanChanged.InvokeAsync(Plan);
    }

    private async Task DuplicateExerciseAsync(ExerciseRowEntry source)
    {
        Objective.ExerciseRows.Add(new ExerciseRowEntry
        {
            SourceItemId = source.SourceItemId,
            SourceCatalogVersion = source.SourceCatalogVersion,
            Category = source.Category,
            InterventionRegion = source.InterventionRegion,
            Notes = source.Notes,
            SuggestedExercise = source.SuggestedExercise,
            ActualExercisePerformed = source.ActualExercisePerformed,
            SetsRepsDuration = source.SetsRepsDuration,
            ResistanceOrWeight = source.ResistanceOrWeight,
            CptCode = source.CptCode,
            CptDescription = source.CptDescription,
            TimeMinutes = source.TimeMinutes,
            AssistanceLevel = source.AssistanceLevel,
            Cueing = source.Cueing,
            IncludeInHomeExerciseProgram = source.IncludeInHomeExerciseProgram,
            IsCheckedSuggestedExercise = source.IsCheckedSuggestedExercise,
            IsSourceBacked = source.IsSourceBacked,
            Prescription = source.Prescription is null ? null : new ExercisePrescriptionEntry
            {
                Sets = source.Prescription.Sets,
                Repetitions = source.Prescription.Repetitions,
                Frequency = source.Prescription.Frequency
            }
        });
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task RemoveExerciseAsync(ExerciseRowEntry exercise)
    {
        Objective.ExerciseRows.Remove(exercise);
        _collapsedExercises.Remove(exercise);
        _exerciseErrors.Remove(exercise);
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private void ToggleExercise(ExerciseRowEntry exercise) => ToggleCollapsed(_collapsedExercises, exercise);

    private async Task UpdateExerciseNumberAsync(ExerciseRowEntry exercise, string field, object? value)
    {
        var errors = GetExerciseErrors(exercise);
        var text = value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetExerciseNumber(exercise, field, null);
            errors.Remove(field);
        }
        else if (!int.TryParse(text, out var parsed) || parsed is < 1 or > 999)
        {
            errors[field] = $"{field} must be a whole number from 1 to 999.";
        }
        else
        {
            SetExerciseNumber(exercise, field, parsed);
            errors.Remove(field);
        }
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private Task UpdateSetsAsync(ExerciseRowEntry exercise, object? value) =>
        UpdateExerciseNumberAsync(exercise, "Sets", value);

    private Task UpdateRepetitionsAsync(ExerciseRowEntry exercise, object? value) =>
        UpdateExerciseNumberAsync(exercise, "Reps", value);

    private async Task UpdateFrequencyAsync(ExerciseRowEntry exercise, object? value)
    {
        var frequency = value?.ToString()?.Trim() ?? string.Empty;
        var errors = GetExerciseErrors(exercise);
        if (frequency.Length > 50 || (frequency.Length > 0 && !frequency.Any(char.IsLetterOrDigit)))
        {
            errors["Frequency"] = "Frequency must be 50 characters or fewer and include a letter or number.";
        }
        else
        {
            GetPrescription(exercise).Frequency = string.IsNullOrWhiteSpace(frequency) ? null : frequency;
            errors.Remove("Frequency");
            await ObjectiveChanged.InvokeAsync(Objective);
        }
    }

    private async Task UpdateExerciseNotesAsync(ExerciseRowEntry exercise, object? value)
    {
        exercise.Notes = NormalizeOptional(value?.ToString());
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task ToggleExerciseHepAsync(ExerciseRowEntry exercise)
    {
        exercise.IncludeInHomeExerciseProgram = !exercise.IncludeInHomeExerciseProgram;
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private ExercisePrescriptionEntry GetPrescription(ExerciseRowEntry exercise) =>
        exercise.Prescription ??= new ExercisePrescriptionEntry();

    private Dictionary<string, string> GetExerciseErrors(ExerciseRowEntry exercise)
    {
        if (!_exerciseErrors.TryGetValue(exercise, out var errors))
        {
            errors = new Dictionary<string, string>(StringComparer.Ordinal);
            _exerciseErrors[exercise] = errors;
        }
        return errors;
    }

    private string? GetExerciseError(ExerciseRowEntry exercise, string field) =>
        _exerciseErrors.TryGetValue(exercise, out var errors)
            ? errors.GetValueOrDefault(field)
            : null;

    private static void SetExerciseNumber(ExerciseRowEntry exercise, string field, int? value)
    {
        var prescription = exercise.Prescription ??= new ExercisePrescriptionEntry();
        if (field == "Sets") prescription.Sets = value;
        else prescription.Repetitions = value;
    }

    private static void ToggleCollapsed<T>(HashSet<T> collapsed, T item) where T : class
    {
        if (!collapsed.Add(item)) collapsed.Remove(item);
    }

    private static string GetExerciseTitle(ExerciseRowEntry exercise) =>
        string.IsNullOrWhiteSpace(exercise.ActualExercisePerformed)
            ? (string.IsNullOrWhiteSpace(exercise.SuggestedExercise) ? "Exercise" : exercise.SuggestedExercise.Trim())
            : exercise.ActualExercisePerformed.Trim();

    private (string Category, string Region) GetExerciseDetails(ExerciseRowEntry exercise) =>
        (string.IsNullOrWhiteSpace(exercise.Category) ? "Exercise" : exercise.Category,
         exercise.InterventionRegion.HasValue ? GetRegionLabel(exercise.InterventionRegion.Value) : ResolveSelectedRegionLabel());

    private static bool IsManualTechnique(GeneralInterventionEntry entry) =>
        entry.Kind == InterventionKind.ManualTechnique ||
        string.Equals(entry.Category, "Manual", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry.Category, "Manual Work Technique", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExerciseSearch(InterventionLibraryItem item, string query)
    {
        var trimmed = query.Trim();
        return trimmed.Length == 0 ||
               item.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
               item.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
               item.SearchAliases.Any(alias => alias.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private InterventionRegion ResolveSelectedRegion() => SelectedBodyPart?.Trim().ToLowerInvariant() switch
    {
        "shoulder" => InterventionRegion.Shoulder,
        "elbow" => InterventionRegion.Elbow,
        "hand" or "wrist" or "hand/wrist" => InterventionRegion.HandWrist,
        "cervical" or "cervical spine" => InterventionRegion.CervicalSpine,
        "lumbar" or "lumbar spine" => InterventionRegion.LumbarSpine,
        "hip" => InterventionRegion.Hip,
        "knee" => InterventionRegion.Knee,
        "ankle" or "foot" or "ankle/foot" => InterventionRegion.AnkleFoot,
        _ => InterventionRegion.General
    };

    private string ResolveSelectedRegionLabel() => GetRegionLabel(ResolveSelectedRegion());
    private static string GetRegionLabel(InterventionRegion region) => Regions.First(item => item.Region == region).Label;
    private static string FormatCount(int count, string singular) => $"{count} {(count == 1 ? singular : singular + "s")}";
    private static string GetTabClass(bool selected) => selected ? "intervention-tab intervention-tab--selected" : "intervention-tab";
    private static string GetRegionFilterClass(bool selected) => selected ? "intervention-region-filter intervention-region-filter--selected" : "intervention-region-filter";
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [JSInvokable]
    public async Task CloseFromJs()
    {
        await CloseDialogAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task CloseDialogAsync()
    {
        await CleanupModalAsync();
        _dialogKind = InterventionDialogKind.None;
        _customExerciseErrors.Clear();
    }

    private async Task CleanupModalAsync()
    {
        if (!_modalAccessibilityActive || _modalModule is null) return;
        try
        {
            await _modalModule.InvokeVoidAsync("unlockBodyScroll");
            await _modalModule.InvokeVoidAsync("unregisterEscapeHandler", _modalId);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        finally { _modalAccessibilityActive = false; }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupModalAsync();
        if (_modalModule is not null)
        {
            try { await _modalModule.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        _dotNetReference?.Dispose();
    }

    private sealed record RegionFilter(InterventionRegion? Region, string Label);

    private enum InterventionDialogKind { None, Exercise, Technique }
    private enum DialogMode { Library, Custom }
}
