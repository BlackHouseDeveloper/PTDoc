using Microsoft.AspNetCore.Components;
using PTDoc.Application.Configurations.Header;

namespace PTDoc.UI.Components.Layout;

public class GlobalPageHeaderBase : ComponentBase
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IHeaderConfigurationService HeaderConfigurationService { get; set; } = default!;

    [Parameter] public HeaderConfiguration? Configuration { get; set; }
    [Parameter] public EventCallback OnPrimaryAction { get; set; }

    protected HeaderConfiguration EffectiveConfiguration { get; private set; } = new();

    protected override void OnParametersSet()
    {
        if (Configuration is not null)
        {
            EffectiveConfiguration = Configuration;
            return;
        }

        var currentUri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
        EffectiveConfiguration = HeaderConfigurationService.GetConfiguration(currentUri.PathAndQuery);
    }

    protected Task HandlePrimaryActionAsync()
    {
        if (OnPrimaryAction.HasDelegate)
        {
            return OnPrimaryAction.InvokeAsync();
        }

        if (!string.IsNullOrWhiteSpace(EffectiveConfiguration.PrimaryActionRoute))
        {
            NavigationManager.NavigateTo(EffectiveConfiguration.PrimaryActionRoute);
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(EffectiveConfiguration.PrimaryActionEventId))
        {
            var currentUri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
            var basePath = currentUri.GetLeftPart(UriPartial.Path);
            NavigationManager.NavigateTo($"{basePath}?action={Uri.EscapeDataString(EffectiveConfiguration.PrimaryActionEventId)}");
        }

        return Task.CompletedTask;
    }
}
