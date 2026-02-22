namespace PTDoc.Application.Configurations.Header;

public enum HeaderLayoutBehavior
{
    Standard,
    Compact
}

public sealed record HeaderConfiguration
{
    public string Route { get; init; } = "/";
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public bool ShowPrimaryAction { get; init; }
    public string? PrimaryActionText { get; init; }
    public string? PrimaryActionRoute { get; init; }
    public string? PrimaryActionEventId { get; init; }
    public bool ShowSyncControls { get; init; }
    public bool ShowStatusBadge { get; init; }
    public bool ShowMenuToggle { get; init; } = true;
    public HeaderLayoutBehavior LayoutBehavior { get; init; } = HeaderLayoutBehavior.Standard;
}