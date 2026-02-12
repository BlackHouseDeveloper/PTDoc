using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PTDoc.Application.Auth;
using PTDoc.Application.Services;
using System.ComponentModel.DataAnnotations;

namespace PTDoc.UI.Pages;

/// <summary>
/// Base class containing shared logic for Login and MAUI Login components
/// </summary>
public abstract class LoginBase : ComponentBase, IDisposable
{
    [Inject] protected IUserService UserService { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected ILogger<LoginBase> Logger { get; set; } = default!;
    [Inject] protected IThemeService ThemeService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    protected string returnUrl = "/";
    protected AuthMode authMode = AuthMode.Login;
    protected readonly LoginModel loginModel = new();
    protected readonly SignUpModel signUpModel = new();
    protected string? errorMessage;
    protected bool isLoading;
    protected bool isDarkTheme;

    protected enum AuthMode
    {
        Login,
        SignUp
    }

    protected override void OnInitialized()
    {
        returnUrl = GetReturnUrl(Navigation.Uri);

        if (Navigation.Uri.Contains("error=1", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Invalid PIN. Please try again.";
        }

        authMode = Navigation.Uri.Contains("/signup", StringComparison.OrdinalIgnoreCase)
            ? AuthMode.SignUp
            : AuthMode.Login;

        // Subscribe to theme changes
        ThemeService.OnThemeChanged += OnThemeChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Initialize theme after component is interactive (JS is available)
            await ThemeService.InitializeAsync();
            isDarkTheme = ThemeService.IsDarkMode;
            StateHasChanged(); // Refresh UI with theme state
        }
    }

    protected async Task ToggleTheme()
    {
        await ThemeService.ToggleAsync();
    }

    private void OnThemeChanged()
    {
        isDarkTheme = ThemeService.IsDarkMode;
        InvokeAsync(StateHasChanged);
    }

    protected void SwitchMode(AuthMode mode)
    {
        if (authMode == mode) return;

        authMode = mode;
        errorMessage = null;

        // Update URL without navigation
        var targetUrl = mode == AuthMode.Login ? "/login" : "/signup";
        Navigation.NavigateTo(targetUrl, forceLoad: false);
    }

    protected async Task HandleLogin()
    {
        Logger.LogInformation("HandleLogin called - Username: {Username}, PIN: {Pin}", 
            loginModel.Username, 
            loginModel.Pin?.Length > 0 ? "****" : "empty");
        
        isLoading = true;
        errorMessage = null;
        StateHasChanged();

        try
        {
            await Task.Delay(200);

            var username = string.IsNullOrWhiteSpace(loginModel.Username)
                ? loginModel.Pin
                : loginModel.Username;

            Logger.LogInformation("Sending to LoginAsync - Username: {Username}, Password: {Password}", 
                username, loginModel.Pin?.Length > 0 ? "****" : "empty");

            var success = await UserService.LoginAsync(username ?? string.Empty, loginModel.Pin ?? string.Empty);

            if (!success)
            {
                errorMessage = "Invalid PIN. Please try again.";
                isLoading = false;
                StateHasChanged();
                return;
            }

            if (UserService.IsAuthenticated)
            {
                Navigation.NavigateTo(returnUrl, forceLoad: false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Login failed");
            errorMessage = "We couldn't sign you in. Please try again.";
            isLoading = false;
            StateHasChanged();
        }
    }

    protected async Task HandleSignUp()
    {
        isLoading = true;
        errorMessage = null;
        StateHasChanged();

        try
        {
            await Task.Delay(500);

            // Call the RegisterAsync method
            var success = await UserService.RegisterAsync(
                signUpModel.FullName,
                signUpModel.Email,
                signUpModel.DateOfBirth!.Value,
                signUpModel.LicenseType,
                signUpModel.LicenseNumber,
                signUpModel.LicenseState);

            if (success)
            {
                Logger.LogInformation("Sign up successful for {Email}", signUpModel.Email);
                // After successful registration, redirect to login
                Navigation.NavigateTo("/login");
            }
            else
            {
                errorMessage = "Unable to create account. Please check your information and try again.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Sign up failed");
            errorMessage = "We couldn't create your account. Please try again.";
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    protected static string GetReturnUrl(string uri)
    {
        var parsed = new Uri(uri);

        if (string.IsNullOrWhiteSpace(parsed.Query))
            return "/";

        var query = parsed.Query.TrimStart('?').Split('&');

        var returnUrl = query
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 &&
                string.Equals(parts[0], "returnUrl", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();

        return returnUrl ?? "/";
    }

    protected sealed class LoginModel
    {
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "PIN is required")]
        [StringLength(4, MinimumLength = 4, ErrorMessage = "PIN must be 4 digits")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be 4 digits")]
        public string Pin { get; set; } = string.Empty;
    }

    protected sealed class SignUpModel
    {
        [Required(ErrorMessage = "Full name is required")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 100 characters")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Date of birth is required")]
        public DateTime? DateOfBirth { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "License type is required")]
        public string LicenseType { get; set; } = string.Empty;

        [Required(ErrorMessage = "License number is required")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "License number must be between 3 and 50 characters")]
        public string LicenseNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "License state is required")]
        public string LicenseState { get; set; } = string.Empty;
    }

    protected static readonly List<(string Code, string Name)> UsStates = new()
    {
        ("AL", "Alabama"), ("AK", "Alaska"), ("AZ", "Arizona"), ("AR", "Arkansas"),
        ("CA", "California"), ("CO", "Colorado"), ("CT", "Connecticut"), ("DE", "Delaware"),
        ("FL", "Florida"), ("GA", "Georgia"), ("HI", "Hawaii"), ("ID", "Idaho"),
        ("IL", "Illinois"), ("IN", "Indiana"), ("IA", "Iowa"), ("KS", "Kansas"),
        ("KY", "Kentucky"), ("LA", "Louisiana"), ("ME", "Maine"), ("MD", "Maryland"),
        ("MA", "Massachusetts"), ("MI", "Michigan"), ("MN", "Minnesota"), ("MS", "Mississippi"),
        ("MO", "Missouri"), ("MT", "Montana"), ("NE", "Nebraska"), ("NV", "Nevada"),
        ("NH", "New Hampshire"), ("NJ", "New Jersey"), ("NM", "New Mexico"), ("NY", "New York"),
        ("NC", "North Carolina"), ("ND", "North Dakota"), ("OH", "Ohio"), ("OK", "Oklahoma"),
        ("OR", "Oregon"), ("PA", "Pennsylvania"), ("RI", "Rhode Island"), ("SC", "South Carolina"),
        ("SD", "South Dakota"), ("TN", "Tennessee"), ("TX", "Texas"), ("UT", "Utah"),
        ("VT", "Vermont"), ("VA", "Virginia"), ("WA", "Washington"), ("WV", "West Virginia"),
        ("WI", "Wisconsin"), ("WY", "Wyoming")
    };

    public void Dispose()
    {
        ThemeService.OnThemeChanged -= OnThemeChanged;
    }
}
