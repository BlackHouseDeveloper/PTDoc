using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class StructuredWorkspaceEditorsTests : TestContext
{
    [Fact]
    public void SubjectiveTab_LoadsCatalogBackedFunctionalLimitations()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var vm = new SubjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString()
        };

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                FunctionalLimitationCategories =
                [
                    new CatalogCategory
                    {
                        Name = "Mobility",
                        Items = ["Difficulty squatting", "Difficulty with stairs"]
                    }
                ]
            });

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<SubjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => vm = updated))
            .Add(component => component.IsReadOnly, false));

        cut.WaitForAssertion(() => Assert.Contains("Difficulty squatting", cut.Markup, StringComparison.Ordinal));

        cut.Find("input[name='functional-limitations']").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(vm.StructuredFunctionalLimitations);
            Assert.Equal("Mobility", vm.StructuredFunctionalLimitations[0].Category);
            Assert.Equal("Difficulty squatting", vm.StructuredFunctionalLimitations[0].Description);
            Assert.Equal(BodyPart.Knee.ToString(), vm.StructuredFunctionalLimitations[0].BodyPart);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_LoadsCatalogBackedEditors()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var outcomeRegistry = new Mock<IOutcomeMeasureRegistry>(MockBehavior.Strict);
        var vm = new ObjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString()
        };

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                NormalRangeOfMotionOptions = ["Knee flexion 0-135"],
                SpecialTestsOptions = ["McMurray"],
                TenderMuscleOptions = ["Quadriceps"],
                ExerciseOptions = ["Heel slides"],
                MmtGradeOptions = ["4/5", "5/5"]
            });

        outcomeRegistry
            .Setup(registry => registry.GetMeasuresForBodyPart(BodyPart.Knee))
            .Returns(Array.Empty<OutcomeMeasureDefinition>());

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(outcomeRegistry.Object);

        var cut = RenderComponent<ObjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<ObjectiveVm>(this, updated => vm = updated))
            .Add(component => component.PatientId, Guid.NewGuid().ToString())
            .Add(component => component.IsReadOnly, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Knee flexion 0-135", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("McMurray", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Heel slides", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button").First(button => button.TextContent.Contains("Knee flexion 0-135", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("McMurray", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Heel slides", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(vm.Metrics);
            Assert.Single(vm.SpecialTests);
            Assert.Single(vm.ExerciseRows);
            Assert.Equal("Heel slides", vm.ExerciseRows[0].SuggestedExercise);
            Assert.True(vm.ExerciseRows[0].IsSourceBacked);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }
}
