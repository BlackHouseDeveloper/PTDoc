using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailyTreatmentEditableTextAreaTests : TestContext
{
    [Fact]
    public void EditableTextArea_InputPropagatesValueChanged()
    {
        var updatedValues = new List<string>();

        var cut = RenderComponent<EditableTextArea>(parameters => parameters
            .Add(component => component.Id, "daily-response-to-treatment")
            .Add(component => component.Value, "Original response")
            .Add(
                component => component.ValueChanged,
                EventCallback.Factory.Create<string>(this, value => updatedValues.Add(value))));

        cut.Find("#daily-response-to-treatment").Input("Tolerated exercise progression without symptom flare.");

        Assert.Single(updatedValues);
        Assert.Equal("Tolerated exercise progression without symptom flare.", updatedValues[0]);
    }
}
