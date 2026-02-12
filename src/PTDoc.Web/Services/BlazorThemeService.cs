using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Web.Services;

/// <summary>
/// Blazor Web implementation of IThemeService
/// Uses localStorage via JS interop for persistence
/// </summary>
public class BlazorThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private ThemeMode _currentTheme = ThemeMode.Light;

    public event Action? OnThemeChanged;

    public ThemeMode Current => _currentTheme;

    public bool IsDarkMode => _currentTheme == ThemeMode.Dark;

    public BlazorThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Initialize theme service - loads persisted theme from localStorage
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Get current theme from DOM (set by JS on page load)
            var theme = await _jsRuntime.InvokeAsync<string>("ptdocTheme.getTheme");
            _currentTheme = theme == "dark" ? ThemeMode.Dark : ThemeMode.Light;
            OnThemeChanged?.Invoke();
        }
        catch (JSException)
        {
            // JS interop not ready yet - will use default Light theme
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
            Console.WriteLine($"[BlazorThemeService] Error toggling theme: {ex.Message}");
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
            Console.WriteLine($"[BlazorThemeService] Error setting theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Legacy: Toggle between light and dark theme
    /// </summary>
    public Task ToggleThemeAsync() => ToggleAsync();
}
