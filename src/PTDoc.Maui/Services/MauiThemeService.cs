using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Maui.Services;

/// <summary>
/// MAUI implementation of IThemeService
/// Uses MAUI Preferences for persistence and hybrid JS interop for DOM manipulation
/// </summary>
public class MauiThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private ThemeMode _currentTheme = ThemeMode.Light;
    private const string THEME_PREFERENCE_KEY = "ptdoc-theme";

    public event Action? OnThemeChanged;

    public ThemeMode Current => _currentTheme;

    public bool IsDarkMode => _currentTheme == ThemeMode.Dark;

    public MauiThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Initialize theme service - loads persisted theme from MAUI Preferences
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Load theme preference from MAUI Preferences
            var savedTheme = Preferences.Get(THEME_PREFERENCE_KEY, "light");
            _currentTheme = savedTheme == "dark" ? ThemeMode.Dark : ThemeMode.Light;

            // Apply theme to WebView DOM
            var themeString = _currentTheme == ThemeMode.Dark ? "dark" : "light";
            await _jsRuntime.InvokeVoidAsync("ptdocTheme.setTheme", themeString);
            
            OnThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MauiThemeService] Error initializing theme: {ex.Message}");
            // Fallback to Light theme
            _currentTheme = ThemeMode.Light;
        }
    }

    /// <summary>
    /// Toggle between light and dark theme
    /// </summary>
    public async Task ToggleAsync()
    {
        try
        {
            // Toggle theme
            var newThemeString = await _jsRuntime.InvokeAsync<string>("ptdocTheme.toggle");
            _currentTheme = newThemeString == "dark" ? ThemeMode.Dark : ThemeMode.Light;
            
            // Persist to MAUI Preferences
            Preferences.Set(THEME_PREFERENCE_KEY, newThemeString);
            
            OnThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MauiThemeService] Error toggling theme: {ex.Message}");
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
            
            // Apply to WebView DOM
            await _jsRuntime.InvokeVoidAsync("ptdocTheme.setTheme", themeString);
            
            // Persist to MAUI Preferences
            Preferences.Set(THEME_PREFERENCE_KEY, themeString);
            
            _currentTheme = theme;
            OnThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MauiThemeService] Error setting theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Legacy: Toggle between light and dark theme
    /// </summary>
    public Task ToggleThemeAsync() => ToggleAsync();
}
