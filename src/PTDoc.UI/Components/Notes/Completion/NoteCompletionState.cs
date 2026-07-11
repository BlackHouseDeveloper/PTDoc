using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Components.Notes.Completion;

public sealed class NoteCompletionState
{
    public static NoteCompletionState Complete(IReadOnlyList<SoapSection> sections) =>
        new([], BuildSectionStates(sections, []));

    public NoteCompletionState(
        IReadOnlyList<MissingRequiredItem> missingItems,
        IReadOnlyDictionary<SoapSection, NoteSectionCompletionState> sectionStates)
    {
        MissingItems = missingItems;
        SectionStates = sectionStates;
    }

    public IReadOnlyList<MissingRequiredItem> MissingItems { get; }

    public IReadOnlyDictionary<SoapSection, NoteSectionCompletionState> SectionStates { get; }

    public bool IsComplete => MissingItems.Count == 0;

    public int MissingCount => MissingItems.Count;

    public MissingRequiredItem? FirstMissingItem => MissingItems.FirstOrDefault();

    public static IReadOnlyDictionary<SoapSection, NoteSectionCompletionState> BuildSectionStates(
        IReadOnlyList<SoapSection> sections,
        IReadOnlyList<MissingRequiredItem> missingItems)
    {
        return sections
            .Distinct()
            .ToDictionary(
                section => section,
                section =>
                {
                    var count = missingItems.Count(item => item.Section == section);
                    return new NoteSectionCompletionState(section, count, count == 0);
                });
    }
}
