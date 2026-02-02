namespace PTDoc.Application.Services;

/// <summary>
/// Service for managing theme state across Blazor components
/// Integrates with JavaScript for DOM manipulation and localStorage persistence
/// </summary>
public interface IThemeService
{
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
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Toggle between light and dark theme
    /// </summary>
    Task ToggleThemeAsync();

    /// <summary>
    /// Set theme to specific value
    /// </summary>
    Task SetThemeAsync(string theme);
}
