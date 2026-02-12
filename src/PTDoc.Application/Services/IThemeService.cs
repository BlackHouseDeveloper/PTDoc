namespace PTDoc.Application.Services;

/// <summary>
/// Represents the available theme modes
/// </summary>
public enum ThemeMode
{
    Light,
    Dark
}

/// <summary>
/// Service for managing theme state across Blazor components
/// Platform-agnostic interface - implementations handle platform-specific persistence
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets the current theme mode
    /// </summary>
    ThemeMode Current { get; }

    /// <summary>
    /// Gets whether dark mode is currently active
    /// </summary>
    bool IsDarkMode { get; }

    /// <summary>
    /// Event raised when theme changes
    /// </summary>
    event Action? OnThemeChanged;

    /// <summary>
    /// Initialize theme service - must be called after render
    /// Loads persisted theme preference and applies it
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Toggle between light and dark theme
    /// </summary>
    Task ToggleAsync();

    /// <summary>
    /// Set theme to specific value
    /// </summary>
    Task SetThemeAsync(ThemeMode theme);

    /// <summary>
    /// Legacy: Toggle between light and dark theme
    /// </summary>
    Task ToggleThemeAsync();
}
