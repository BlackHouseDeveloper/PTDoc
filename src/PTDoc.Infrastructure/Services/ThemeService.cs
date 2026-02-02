using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Service for managing theme state across Blazor components
/// Integrates with JavaScript theme.js for DOM manipulation and localStorage persistence
/// </summary>
public class ThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _isDarkMode;

    public event Action? OnThemeChanged;

    public bool IsDarkMode => _isDarkMode;

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
            _isDarkMode = theme == "dark";
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
    public async Task ToggleThemeAsync()
    {
        try
        {
            var newTheme = await _jsRuntime.InvokeAsync<string>("ptdocTheme.toggle");
            _isDarkMode = newTheme == "dark";
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
    public async Task SetThemeAsync(string theme)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("ptdocTheme.setTheme", theme);
            _isDarkMode = theme == "dark";
            OnThemeChanged?.Invoke();
        }
        catch (JSException ex)
        {
            Console.WriteLine($"Error setting theme: {ex.Message}");
        }
    }
}
