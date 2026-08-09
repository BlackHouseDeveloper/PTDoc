using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Components.Notes.Workspace.Evaluation;

public partial class InterventionLibrarySection
{
    private static readonly IReadOnlyList<string> Regions =
    [
        "All", "Shoulder", "Elbow", "Hand/Wrist", "Cervical Spine",
        "Lumbar Spine", "Hip", "Knee", "Ankle/Foot", "General"
    ];

    // TODO: Replace document fixtures through an approved frontend library interface when its data source is defined.
    private static readonly IReadOnlyList<LibraryItem> ExerciseLibrary =
    [
        new("Pendulum Exercise", "Range of Motion", "Shoulder"),
        new("Shoulder Flexion (Active)", "Range of Motion", "Shoulder"),
        new("Shoulder Abduction (Active)", "Range of Motion", "Shoulder"),
        new("External Rotation with Band", "Strengthening", "Shoulder"),
        new("Internal Rotation with Band", "Strengthening", "Shoulder"),
        new("Scapular Retraction", "Strengthening", "Shoulder")
    ];

    private static readonly IReadOnlyList<LibraryItem> TechniqueLibrary =
    [
        new("Glenohumeral Joint Mobilization", "Manual Work Technique", "Shoulder"),
        new("Soft Tissue Mobilization - Rotator Cuff", "Manual Work Technique", "Shoulder"),
        new("Myofascial Release - Shoulder Complex", "Manual Work Technique", "Shoulder"),
        new("Scapular Mobilization", "Manual Work Technique", "Shoulder"),
        new("Cross-friction Massage - Shoulder", "Manual Work Technique", "Shoulder"),
        new("Elbow Joint Mobilization", "Manual Work Technique", "Elbow"),
        new("Soft Tissue Mobilization - Forearm", "Manual Work Technique", "Elbow")
    ];

    private readonly Dictionary<ExerciseRowEntry, PrescriptionDraft> _prescriptions = new();
    private readonly HashSet<ExerciseRowEntry> _collapsedExercises = new();
    private readonly string _modalId = $"intervention-library-dialog-{Guid.NewGuid():N}";
    private InterventionDialogKind _dialogKind;
    private DialogMode _exerciseMode = DialogMode.Library;
    private DialogMode _techniqueMode = DialogMode.Library;
    private string _exerciseSearchQuery = string.Empty;
    private string _exerciseRegion = "All";
    private string _techniqueRegion = "All";
    private string _customExerciseName = string.Empty;
    private string _customExerciseNotes = string.Empty;
    private IJSObjectReference? _modalModule;
    private DotNetObjectReference<InterventionLibrarySection>? _dotNetReference;
    private bool _modalAccessibilityActive;

    [Parameter, EditorRequired]
    public ObjectiveVm Objective { get; set; } = new();

    [Parameter]
    public EventCallback<ObjectiveVm> ObjectiveChanged { get; set; }

    [Parameter, EditorRequired]
    public PlanVm Plan { get; set; } = new();

    [Parameter]
    public EventCallback<PlanVm> PlanChanged { get; set; }

    [Parameter]
    public bool IsReadOnly { get; set; }

    [Parameter]
    public string? SelectedBodyPart { get; set; }

    private int ManualTechniqueCount => Plan.GeneralInterventions.Count(IsManualTechnique);

    private string DialogTitle => _dialogKind == InterventionDialogKind.Exercise
        ? "Add Therapeutic Exercise"
        : "Add Manual Technique";

    private string DialogDescription => _dialogKind == InterventionDialogKind.Exercise
        ? "Select from library or add custom exercise"
        : "Select from library or add custom technique";

    private IEnumerable<LibraryItem> FilteredExercises => ExerciseLibrary.Where(item =>
        MatchesRegion(item, _exerciseRegion)
        && (string.IsNullOrWhiteSpace(_exerciseSearchQuery)
            || item.Name.Contains(_exerciseSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(_exerciseSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase)));

    private IEnumerable<LibraryItem> FilteredTechniques => TechniqueLibrary.Where(item => MatchesRegion(item, _techniqueRegion));

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
            catch (JSDisconnectedException)
            {
                // The circuit may close while the dialog is opening.
            }
            catch (JSException)
            {
                // The native dialog remains usable if optional focus-management JS is unavailable.
            }
            catch (InvalidOperationException)
            {
                // JavaScript is unavailable during static rendering and unit tests.
            }
        }
    }

    private async Task OpenExerciseDialogAsync()
    {
        await CleanupModalAsync();
        _dialogKind = InterventionDialogKind.Exercise;
        _exerciseMode = DialogMode.Library;
        _exerciseSearchQuery = string.Empty;
        _exerciseRegion = "All";
    }

    private async Task OpenTechniqueDialogAsync()
    {
        await CleanupModalAsync();
        _dialogKind = InterventionDialogKind.Technique;
        _techniqueMode = DialogMode.Library;
        _techniqueRegion = "All";
    }

    private void SelectExerciseRegion(string region) => _exerciseRegion = region;

    private void SelectTechniqueRegion(string region) => _techniqueRegion = region;

    private async Task AddLibraryExerciseAsync(LibraryItem item)
    {
        var row = new ExerciseRowEntry
        {
            SuggestedExercise = item.Name,
            ActualExercisePerformed = item.Name,
            IsCheckedSuggestedExercise = true,
            IsSourceBacked = true
        };
        Objective.ExerciseRows.Add(row);
        _prescriptions[row] = PrescriptionDraft.Default;
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task AddCustomExerciseAsync()
    {
        if (string.IsNullOrWhiteSpace(_customExerciseName))
        {
            return;
        }

        var row = new ExerciseRowEntry
        {
            SuggestedExercise = _customExerciseName.Trim(),
            ActualExercisePerformed = _customExerciseName.Trim(),
            IsCheckedSuggestedExercise = true
        };
        Objective.ExerciseRows.Add(row);
        _prescriptions[row] = PrescriptionDraft.Default;
        // TODO: Persist custom exercise notes when the workspace contract defines the field.
        _customExerciseName = string.Empty;
        _customExerciseNotes = string.Empty;
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task AddLibraryTechniqueAsync(LibraryItem item)
    {
        Plan.GeneralInterventions.Add(new GeneralInterventionEntry
        {
            Name = item.Name,
            Category = "Manual Work Technique",
            IsSourceBacked = true
        });
        await PlanChanged.InvokeAsync(Plan);
    }

    private async Task DuplicateExerciseAsync(ExerciseRowEntry source)
    {
        var duplicate = new ExerciseRowEntry
        {
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
            IsSourceBacked = source.IsSourceBacked
        };
        Objective.ExerciseRows.Add(duplicate);
        _prescriptions[duplicate] = GetPrescription(source).Clone();
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private async Task RemoveExerciseAsync(ExerciseRowEntry exercise)
    {
        Objective.ExerciseRows.Remove(exercise);
        _collapsedExercises.Remove(exercise);
        _prescriptions.Remove(exercise);
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private void ToggleExercise(ExerciseRowEntry exercise)
    {
        if (!_collapsedExercises.Add(exercise))
        {
            _collapsedExercises.Remove(exercise);
        }
    }

    private async Task ToggleHepAsync(ExerciseRowEntry exercise)
    {
        exercise.IncludeInHomeExerciseProgram = !exercise.IncludeInHomeExerciseProgram;
        await ObjectiveChanged.InvokeAsync(Objective);
    }

    private PrescriptionDraft GetPrescription(ExerciseRowEntry exercise)
    {
        if (!_prescriptions.TryGetValue(exercise, out var draft))
        {
            // TODO: Map Sets, Reps, and Frequency to an approved persisted schema when defined.
            draft = PrescriptionDraft.FromExisting(exercise.SetsRepsDuration);
            _prescriptions[exercise] = draft;
        }
        return draft;
    }

    private static string GetExerciseTitle(ExerciseRowEntry exercise) =>
        string.IsNullOrWhiteSpace(exercise.ActualExercisePerformed)
            ? (string.IsNullOrWhiteSpace(exercise.SuggestedExercise) ? "Exercise" : exercise.SuggestedExercise.Trim())
            : exercise.ActualExercisePerformed.Trim();

    private (string Category, string Region) GetExerciseDetails(ExerciseRowEntry exercise)
    {
        var fixture = ExerciseLibrary.FirstOrDefault(item => string.Equals(item.Name, GetExerciseTitle(exercise), StringComparison.OrdinalIgnoreCase));
        var selectedRegion = SelectedBodyPart;
        return fixture is null
            ? ("Exercise", string.IsNullOrWhiteSpace(selectedRegion) ? "General" : selectedRegion.Trim())
            : (fixture.Category, fixture.Region);
    }

    private static bool IsManualTechnique(GeneralInterventionEntry entry) =>
        string.Equals(entry.Category, "Manual Work Technique", StringComparison.OrdinalIgnoreCase)
        || TechniqueLibrary.Any(item => string.Equals(item.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesRegion(LibraryItem item, string region) =>
        string.Equals(region, "All", StringComparison.Ordinal)
        || string.Equals(item.Region, region, StringComparison.OrdinalIgnoreCase);

    private static string FormatCount(int count, string singular) => $"{count} {(count == 1 ? singular : singular + "s")}";

    private static string GetTabClass(bool selected) => selected ? "intervention-tab intervention-tab--selected" : "intervention-tab";

    private static string GetRegionFilterClass(bool selected) => selected
        ? "intervention-region-filter intervention-region-filter--selected"
        : "intervention-region-filter";

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
    }

    private async Task CleanupModalAsync()
    {
        if (!_modalAccessibilityActive || _modalModule is null)
        {
            return;
        }

        try
        {
            await _modalModule.InvokeVoidAsync("unlockBodyScroll");
            await _modalModule.InvokeVoidAsync("unregisterEscapeHandler", _modalId);
        }
        catch (JSDisconnectedException)
        {
            // The circuit may close while the dialog is being dismissed.
        }
        catch (JSException)
        {
            // Cleanup must not prevent the local dialog state from closing.
        }
        finally
        {
            _modalAccessibilityActive = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupModalAsync();
        if (_modalModule is not null)
        {
            try
            {
                await _modalModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Ignore JS cleanup failures during circuit shutdown.
            }
        }
        _dotNetReference?.Dispose();
    }

    private sealed record LibraryItem(string Name, string Category, string Region);

    private sealed class PrescriptionDraft
    {
        public string Sets { get; set; } = string.Empty;
        public string Reps { get; set; } = string.Empty;
        public string Frequency { get; set; } = string.Empty;

        public static PrescriptionDraft Default => new() { Sets = "3", Reps = "10", Frequency = "3x/week" };

        public static PrescriptionDraft FromExisting(string? existing) => string.IsNullOrWhiteSpace(existing)
            ? Default
            : new PrescriptionDraft { Sets = existing };

        public PrescriptionDraft Clone() => new() { Sets = Sets, Reps = Reps, Frequency = Frequency };
    }

    private enum InterventionDialogKind
    {
        None,
        Exercise,
        Technique
    }

    private enum DialogMode
    {
        Library,
        Custom
    }
}
