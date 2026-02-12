using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// [DEPRECATED] Generic theme service - replaced by platform-specific implementations
/// Use BlazorThemeService (PTDoc.Web) or MauiThemeService (PTDoc.Maui) instead
/// This class is kept for backward compatibility but should not be used in new code
/// </summary>
[Obsolete("Use BlazorThemeService (PTDoc.Web) or MauiThemeService (PTDoc.Maui) for platform-specific theme management")]
public class ThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private ThemeMode _currentTheme = ThemeMode.Light;

    public event Action? OnThemeChanged;

    public ThemeMode Current => _currentTheme;

    public bool IsDarkMode => _currentTheme == ThemeMode.Dark;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Initialize theme service - must be called after render
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Get current theme from DOM
            var theme = await _jsRuntime.InvokeAsync<string>("ptdocTheme.getTheme");
            _currentTheme = theme == "dark" ? ThemeMode.Dark : ThemeMode.Light;
            OnThemeChanged?.Invoke();
        }
        catch (JSException)
        {
            // JS interop not ready yet, will retry
        }
    }

    /// <summary>
    /// Toggle between light and dark theme
    /// </summary>
    public async Task ToggleAsync()
    {
        try
        {
            var newTheme = await _jsRuntime.InvokeAsync<string>("ptdocTheme.toggle");
            _currentTheme = newTheme == "dark" ? ThemeMode.Dark : ThemeMode.Light;
            OnThemeChanged?.Invoke();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error toggling theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Set theme to specific value
    /// </summary>
    public async Task SetThemeAsync(ThemeMode theme)
    {
        try
        {
            var themeString = theme == ThemeMode.Dark ? "dark" : "light";
            await _jsRuntime.InvokeVoidAsync("ptdocTheme.setTheme", themeString);
            _currentTheme = theme;
            OnThemeChanged?.Invoke();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error setting theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Legacy: Toggle between light and dark theme
    /// </summary>
    public Task ToggleThemeAsync() => ToggleAsync();
}
