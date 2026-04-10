using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

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
    protected List<ClinicSummary> clinics = new();
    protected List<RoleSummary> roles = new();
    protected string? errorMessage;
    protected bool isPendingApprovalNotice;
    protected bool isLoading;
    protected bool isExternalLoginRedirecting;
    protected bool isSubmitting;
    protected bool isPtaFieldsActive;
    protected bool showPendingConfirmation;
    protected bool isDarkTheme;
    protected bool supportsExternalIdentityLogin => UserService.SupportsExternalIdentityLogin;
    protected string DateOfBirthInputValue => signUpModel.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    protected string registrationConfirmationTitle = "Registration submitted";
    protected string registrationConfirmationMessage = "Your account is pending admin approval. You can sign in once your clinic administrator activates access.";
    private bool _pendingLoginFieldReset;

    protected enum AuthMode
    {
        Login,
        SignUp
    }

    protected override void OnInitialized()
    {
        returnUrl = ReturnUrlValidator.ExtractFromUri(Navigation.Uri);
        ApplyAuthStateFromUri(Navigation.Uri);

        // Subscribe to theme changes
        ThemeService.OnThemeChanged += OnThemeChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        if (!UserService.SupportsSelfServiceRegistration)
        {
            return;
        }

        try
        {
            clinics = (await UserService.GetClinicsForSignupAsync()).ToList();
            roles = (await UserService.GetRolesForSignupAsync()).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load signup lookups");
            errorMessage = "Unable to load registration options right now. Please try again.";
        }
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

        if (authMode == AuthMode.Login && (firstRender || _pendingLoginFieldReset))
        {
            _pendingLoginFieldReset = false;
            await ResetLoginFieldsAsync();
        }
    }

    protected async Task ToggleTheme()
    {
        await ThemeService.ToggleAsync();
    }

    protected async Task HandleExternalLogin()
    {
        isExternalLoginRedirecting = true;
        errorMessage = null;
        StateHasChanged();

        try
        {
            var started = await UserService.BeginExternalLoginAsync(returnUrl);
            if (!started)
            {
                errorMessage = "External sign-in is not available right now.";
                isExternalLoginRedirecting = false;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "External login failed to start");
            errorMessage = "We couldn't start external sign-in. Please try again.";
            isExternalLoginRedirecting = false;
            StateHasChanged();
        }
    }

    private void OnThemeChanged()
    {
        isDarkTheme = ThemeService.IsDarkMode;
        InvokeAsync(StateHasChanged);
    }

    protected void SwitchMode(AuthMode mode)
    {
        if (mode == AuthMode.SignUp && !UserService.SupportsSelfServiceRegistration)
        {
            return;
        }

        if (authMode == mode) return;

        authMode = mode;
        errorMessage = null;
        showPendingConfirmation = false;
        isPendingApprovalNotice = false;
        isExternalLoginRedirecting = false;
        registrationConfirmationTitle = "Registration submitted";
        registrationConfirmationMessage = "Your account is pending admin approval. You can sign in once your clinic administrator activates access.";

        if (mode == AuthMode.Login)
        {
            ResetLoginModel();
            _pendingLoginFieldReset = true;
        }
        else
        {
            ResetSignUpModel();
        }

        // Update URL without navigation
        var targetUrl = mode == AuthMode.Login ? "/login" : "/signup";
        Navigation.NavigateTo(targetUrl, forceLoad: false);
    }

    protected async Task HandleLogin()
    {
        Logger.LogInformation("HandleLogin called - Username: {Username}, PIN: {Pin}", 
            loginModel.Username, 
            loginModel.Pin?.Length > 0 ? "****" : "empty");

        if (string.IsNullOrWhiteSpace(loginModel.Username))
        {
            errorMessage = "Username or email is required.";
            isLoading = false;
            StateHasChanged();
            return;
        }

        if (string.IsNullOrWhiteSpace(loginModel.Pin))
        {
            errorMessage = "PIN is required.";
            isLoading = false;
            StateHasChanged();
            return;
        }

        if (loginModel.Pin.Length != 4 || loginModel.Pin.Any(static ch => !char.IsDigit(ch)))
        {
            errorMessage = "PIN must be 4 digits.";
            isLoading = false;
            StateHasChanged();
            return;
        }
        
        isLoading = true;
        errorMessage = null;
        StateHasChanged();

        try
        {
            await Task.Delay(200);

            var username = loginModel.Username.Trim();

            Logger.LogInformation("Sending to LoginAsync - Username: {Username}, Password: {Password}", 
                username, loginModel.Pin?.Length > 0 ? "****" : "empty");

            var success = await UserService.LoginAsync(username ?? string.Empty, loginModel.Pin ?? string.Empty, returnUrl);

            if (!success)
            {
                errorMessage = "Invalid credentials. Please try again.";
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
        isSubmitting = true;
        errorMessage = null;
        showPendingConfirmation = false;
        StateHasChanged();

        try
        {
            if (signUpModel.ClinicId is null || signUpModel.ClinicId == Guid.Empty)
            {
                errorMessage = "Please select a clinic.";
                return;
            }

            if (string.IsNullOrWhiteSpace(signUpModel.RoleKey))
            {
                errorMessage = "Please select a role.";
                return;
            }

            if (signUpModel.Pin != signUpModel.ConfirmPin)
            {
                errorMessage = "PIN and confirmation PIN do not match.";
                return;
            }

            if (isPtaFieldsActive &&
                (string.IsNullOrWhiteSpace(signUpModel.LicenseNumber)
                || string.IsNullOrWhiteSpace(signUpModel.LicenseState)))
            {
                errorMessage = "License number and state are required for PT/PTA roles.";
                return;
            }

            // Call the RegisterAsync method
            var result = await UserService.RegisterAsync(
                signUpModel.FullName,
                signUpModel.Email,
                signUpModel.DateOfBirth!.Value,
                signUpModel.RoleKey,
                signUpModel.ClinicId,
                signUpModel.Pin,
                signUpModel.LicenseNumber,
                signUpModel.LicenseState);

            if (result.IsPending || result.Succeeded)
            {
                Logger.LogInformation("Sign up successful for {Email}", signUpModel.Email);
                registrationConfirmationTitle = result.IsPending ? "Registration submitted" : "Account created";
                registrationConfirmationMessage = result.IsPending
                    ? "Your account is pending admin approval. You can sign in once your clinic administrator activates access."
                    : "Your account was created successfully. Return to Login to continue.";
                ResetSignUpModel();
                showPendingConfirmation = true;
            }
            else
            {
                errorMessage = result.Status switch
                {
                    RegistrationStatus.EmailAlreadyExists => "An account with that email already exists.",
                    RegistrationStatus.InvalidPin => "PIN must be exactly 4 digits.",
                    RegistrationStatus.InvalidLicenseData => "License information is required for PT/PTA roles.",
                    RegistrationStatus.ClinicNotFound => "Selected clinic is invalid.",
                    RegistrationStatus.ValidationFailed => result.Error ?? "Please complete the required registration fields.",
                    RegistrationStatus.UsernameCollision => "Unable to create a unique username. Please contact support.",
                    RegistrationStatus.Succeeded => "Your account was created successfully. Return to Login to continue.",
                    _ => result.Error ?? "Unable to create account. Please check your information and try again."
                };
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
            isSubmitting = false;
            StateHasChanged();
        }
    }

    protected void OnRoleChanged(string roleKey)
    {
        isPtaFieldsActive = string.Equals(roleKey, "PT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleKey, "PTA", StringComparison.OrdinalIgnoreCase);

        if (!isPtaFieldsActive)
        {
            signUpModel.LicenseNumber = string.Empty;
            signUpModel.LicenseState = string.Empty;
        }
    }

    protected Task OnRoleChangedAfterBind()
    {
        OnRoleChanged(signUpModel.RoleKey);
        return Task.CompletedTask;
    }

    protected void OnDateOfBirthChanged(ChangeEventArgs args)
    {
        var rawValue = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            signUpModel.DateOfBirth = null;
            return;
        }

        if (DateTime.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate)
            || DateTime.TryParse(rawValue, CultureInfo.CurrentCulture, DateTimeStyles.None, out exactDate)
            || DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out exactDate))
        {
            signUpModel.DateOfBirth = exactDate.Date;
            return;
        }

        signUpModel.DateOfBirth = null;
    }

    private void ApplyAuthStateFromUri(string uri)
    {
        errorMessage = null;
        isPendingApprovalNotice = false;

        if (uri.Contains("auth_unavailable=1", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Authentication service is currently unavailable. Please try again in a moment.";
        }
        else if (uri.Contains("error=1", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Invalid credentials. Please try again.";
        }
        else if (uri.Contains("pending_approval=1", StringComparison.OrdinalIgnoreCase))
        {
            isPendingApprovalNotice = true;
        }

        authMode = uri.Contains("/signup", StringComparison.OrdinalIgnoreCase)
            ? AuthMode.SignUp
            : AuthMode.Login;

        if (authMode == AuthMode.Login)
        {
            ResetLoginModel();
            _pendingLoginFieldReset = true;
        }
    }

    private void ResetLoginModel()
    {
        loginModel.Username = string.Empty;
        loginModel.Pin = string.Empty;
    }

    private void ResetSignUpModel()
    {
        signUpModel.FullName = string.Empty;
        signUpModel.DateOfBirth = null;
        signUpModel.Email = string.Empty;
        signUpModel.RoleKey = string.Empty;
        signUpModel.ClinicId = null;
        signUpModel.Pin = string.Empty;
        signUpModel.ConfirmPin = string.Empty;
        signUpModel.LicenseNumber = string.Empty;
        signUpModel.LicenseState = string.Empty;
        isPtaFieldsActive = false;
    }

    private async Task ResetLoginFieldsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("ptdocAuth.resetLoginFields");
        }
        catch (JSDisconnectedException)
        {
            // The page is navigating away; a stale login field reset is no longer relevant.
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogDebug(ex, "Login field reset skipped because the browser DOM is not ready.");
        }
        catch (JSException ex)
        {
            Logger.LogDebug(ex, "Login field reset skipped because the auth browser helper is unavailable.");
        }
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

        [Required(ErrorMessage = "Role is required")]
        public string RoleKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Clinic is required")]
        public Guid? ClinicId { get; set; }

        [Required(ErrorMessage = "PIN is required")]
        [StringLength(4, MinimumLength = 4, ErrorMessage = "PIN must be 4 digits")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be 4 digits")]
        public string Pin { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm PIN is required")]
        [StringLength(4, MinimumLength = 4, ErrorMessage = "PIN must be 4 digits")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be 4 digits")]
        public string ConfirmPin { get; set; } = string.Empty;

        public string LicenseNumber { get; set; } = string.Empty;

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
