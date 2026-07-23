using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
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
    protected SignUpModel signUpModel = new();
    protected EditContext signUpEditContext = default!;
    private ValidationMessageStore signUpServerValidationMessages = default!;
    protected readonly ForgotPasswordModel forgotPasswordModel = new();
    protected List<ClinicSummary> clinics = new();
    protected List<RoleSummary> roles = new();
    protected string? errorMessage;
    protected bool isPendingApprovalNotice;
    protected bool isLoading;
    protected bool isExternalLoginRedirecting;
    protected bool isSubmitting;
    protected bool isPtaFieldsActive;
    protected bool showPendingConfirmation;
    protected bool showPasswordResetConfirmation;
    protected bool isDarkTheme;
    protected readonly Dictionary<string, string> loginFieldErrors = new(StringComparer.Ordinal);
    protected int forgotPasswordFormKey;
    protected int signUpFormKey;
    protected bool supportsExternalIdentityLogin => UserService.SupportsExternalIdentityLogin;
    protected string AuthPageTitle => authMode switch
    {
        AuthMode.SignUp => "Sign Up",
        AuthMode.ForgotPassword => "Forgot PIN",
        _ => "Login"
    };
    protected string AuthFailureTitle => authMode switch
    {
        AuthMode.SignUp => "Sign up failed",
        AuthMode.ForgotPassword => "Reset request failed",
        _ => "Login failed"
    };
    protected const string PasswordResetConfirmationMessage =
        "If an account matches that contact method, a secure reset link has been sent.";
    protected string DateOfBirthInputValue => signUpModel.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    protected string ForgotContactLabel => string.Equals(forgotPasswordModel.Channel, "sms", StringComparison.OrdinalIgnoreCase)
        ? "Mobile Number"
        : "Email Address";
    protected string ForgotContactPlaceholder => string.Equals(forgotPasswordModel.Channel, "sms", StringComparison.OrdinalIgnoreCase)
        ? "Enter your mobile number"
        : "Enter your email address";
    protected string ForgotContactAutocomplete => string.Equals(forgotPasswordModel.Channel, "sms", StringComparison.OrdinalIgnoreCase)
        ? "tel"
        : "email";
    protected string LoginReturnUrl => returnUrl;
    protected string registrationConfirmationTitle = "Registration submitted";
    protected string registrationConfirmationMessage = PendingApprovalMessage;
    protected const string PendingApprovalMessage =
        "Your account has been created and is waiting for administrator approval.";
    private bool _pendingLoginFieldReset;
    private bool _pendingSignUpFocus;
    private static readonly string[] SignUpValidationFieldNames =
    [
        nameof(SignUpModel.FullName),
        nameof(SignUpModel.DateOfBirth),
        nameof(SignUpModel.Email),
        nameof(SignUpModel.RoleKey),
        nameof(SignUpModel.ClinicId),
        nameof(SignUpModel.Pin),
        nameof(SignUpModel.ConfirmPin),
        nameof(SignUpModel.LicenseNumber),
        nameof(SignUpModel.LicenseState)
    ];

    protected LoginBase()
    {
        InitializeSignUpEditContext();
    }

    protected enum AuthMode
    {
        Login,
        SignUp,
        ForgotPassword
    }

    protected override void OnInitialized()
    {
        returnUrl = ReturnUrlValidator.ExtractFromUri(Navigation.Uri);
        ApplyAuthStateFromUri(Navigation.Uri);
        Navigation.LocationChanged += OnLocationChanged;

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

        if (_pendingSignUpFocus)
        {
            _pendingSignUpFocus = false;
            await FocusFirstInvalidSignUpFieldAsync();
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

    protected async Task HandleLoginSubmitAsync()
    {
        errorMessage = null;
        loginFieldErrors.Clear();

        var username = loginModel.Username?.Trim() ?? string.Empty;
        var pin = loginModel.Pin?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username))
        {
            loginFieldErrors[nameof(loginModel.Username)] = "Username or email is required.";
        }

        if (string.IsNullOrWhiteSpace(pin))
        {
            loginFieldErrors[nameof(loginModel.Pin)] = "PIN is required.";
        }
        else if (pin.Length != 4 || pin.Any(static ch => !char.IsDigit(ch)))
        {
            loginFieldErrors[nameof(loginModel.Pin)] = "PIN must be 4 digits.";
        }

        if (loginFieldErrors.Count > 0)
        {
            isLoading = false;
            return;
        }

        isLoading = true;
        StateHasChanged();

        try
        {
            await JS.InvokeVoidAsync("ptdocAuth.submitLogin", username, pin, LoginReturnUrl);
        }
        catch (JSDisconnectedException)
        {
            // The browser is navigating or the circuit was disconnected during submit.
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "Login form helper failed.");
            errorMessage = "Unable to submit login right now. Please try again.";
            isLoading = false;
        }
    }

    protected bool HasLoginFieldError(string fieldName) =>
        loginFieldErrors.ContainsKey(fieldName);

    protected string? GetLoginFieldError(string fieldName) =>
        loginFieldErrors.TryGetValue(fieldName, out var error) ? error : null;

    protected string BuildLoginInputClass(string fieldName) =>
        HasLoginFieldError(fieldName) ? "auth-input auth-input--invalid" : "auth-input";

    private void OnThemeChanged()
    {
        isDarkTheme = ThemeService.IsDarkMode;
        InvokeAsync(StateHasChanged);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        ApplyAuthStateFromUri(args.Location);
        _ = InvokeAsync(StateHasChanged);
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
        showPasswordResetConfirmation = false;
        isPendingApprovalNotice = false;
        isExternalLoginRedirecting = false;
        registrationConfirmationTitle = "Registration submitted";
        registrationConfirmationMessage = PendingApprovalMessage;

        if (mode == AuthMode.Login)
        {
            ResetLoginModel();
            _pendingLoginFieldReset = true;
        }
        else if (mode == AuthMode.SignUp)
        {
            ResetSignUpModel();
        }
        else
        {
            ResetForgotPasswordModel();
        }

        // Update URL without navigation
        var targetUrl = mode switch
        {
            AuthMode.SignUp => "/signup",
            AuthMode.ForgotPassword => "/forgot-password",
            _ => "/login"
        };
        Navigation.NavigateTo(targetUrl, forceLoad: false);
    }

    protected async Task HandleLogin()
    {
        Logger.LogDebug("HandleLogin submitted.");

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
        ClearSignUpServerValidationMessages();
        StateHasChanged();

        try
        {
            var result = await UserService.RegisterAsync(
                signUpModel.FullName.Trim(),
                signUpModel.Email.Trim(),
                signUpModel.DateOfBirth!.Value,
                signUpModel.RoleKey.Trim(),
                signUpModel.ClinicId,
                signUpModel.Pin,
                signUpModel.LicenseNumber.Trim(),
                signUpModel.LicenseState.Trim());

            if (result.IsPending || result.Succeeded)
            {
                Logger.LogInformation("Sign up completed successfully.");
                registrationConfirmationTitle = result.IsPending ? "Registration submitted" : "Account created";
                registrationConfirmationMessage = result.IsPending
                    ? PendingApprovalMessage
                    : "Your account was created successfully. Return to Login to continue.";
                ResetSignUpModel();
                showPendingConfirmation = true;
            }
            else
            {
                ApplySignUpServerValidation(result);
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

                if (HasSignUpValidationErrors)
                {
                    _pendingSignUpFocus = true;
                }
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

    protected async Task HandleForgotPassword()
    {
        if (string.IsNullOrWhiteSpace(forgotPasswordModel.Contact))
        {
            errorMessage = string.Equals(forgotPasswordModel.Channel, "sms", StringComparison.OrdinalIgnoreCase)
                ? "Enter a mobile number."
                : "Enter an email address.";
            return;
        }

        if (string.Equals(forgotPasswordModel.Channel, "email", StringComparison.OrdinalIgnoreCase) &&
            !forgotPasswordModel.Contact.Contains('@', StringComparison.Ordinal))
        {
            errorMessage = "Enter a valid email address.";
            return;
        }

        if (string.Equals(forgotPasswordModel.Channel, "sms", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(forgotPasswordModel.Contact.Where(char.IsDigit).ToArray());
            if (digits.Length is not (10 or 11))
            {
                errorMessage = "Enter a valid mobile number.";
                return;
            }
        }

        if (!string.Equals(forgotPasswordModel.Channel, "email", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(forgotPasswordModel.Channel, "sms", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Choose email or SMS delivery.";
            return;
        }

        isLoading = true;
        errorMessage = null;
        showPasswordResetConfirmation = false;
        StateHasChanged();

        try
        {
            var accepted = await UserService.RequestPasswordResetAsync(
                forgotPasswordModel.Contact.Trim(),
                forgotPasswordModel.Channel,
                CancellationToken.None);

            if (accepted)
            {
                ResetForgotPasswordModel();
                showPasswordResetConfirmation = true;
            }
            else
            {
                errorMessage = "We couldn't submit that request right now. Please try again.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Password reset request failed");
            errorMessage = "We couldn't submit that request right now. Please try again.";
        }
        finally
        {
            isLoading = false;
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

    protected Task OnForgotPasswordChannelChangedAfterBind()
    {
        forgotPasswordModel.Contact = string.Empty;
        errorMessage = null;
        showPasswordResetConfirmation = false;
        forgotPasswordFormKey++;
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected void OnDateOfBirthChanged(ChangeEventArgs args)
    {
        var rawValue = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            signUpModel.DateOfBirth = null;
            signUpEditContext.NotifyFieldChanged(new FieldIdentifier(signUpModel, nameof(signUpModel.DateOfBirth)));
            return;
        }

        if (DateTime.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate)
            || DateTime.TryParse(rawValue, CultureInfo.CurrentCulture, DateTimeStyles.None, out exactDate)
            || DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out exactDate))
        {
            signUpModel.DateOfBirth = exactDate.Date;
            signUpEditContext.NotifyFieldChanged(new FieldIdentifier(signUpModel, nameof(signUpModel.DateOfBirth)));
            return;
        }

        signUpModel.DateOfBirth = null;
        signUpEditContext.NotifyFieldChanged(new FieldIdentifier(signUpModel, nameof(signUpModel.DateOfBirth)));
    }

    protected Task HandleInvalidSignUpSubmit(EditContext _)
    {
        errorMessage = null;
        _pendingSignUpFocus = true;
        return Task.CompletedTask;
    }

    protected bool HasSignUpFieldError(string fieldName) =>
        signUpEditContext.GetValidationMessages(new FieldIdentifier(signUpModel, fieldName)).Any();

    protected bool HasSignUpValidationErrors => SignUpValidationFieldNames.Any(HasSignUpFieldError);

    protected string BuildSignUpInputClass(string fieldName) =>
        HasSignUpFieldError(fieldName) ? "auth-input auth-input--invalid" : "auth-input";

    protected string BuildSignUpSelectClass(string fieldName) =>
        $"{BuildSignUpInputClass(fieldName)} auth-select";

    protected string? BuildSignUpAriaDescribedBy(string fieldName, string? helpId, string validationId)
    {
        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(helpId))
        {
            ids.Add(helpId);
        }

        if (HasSignUpFieldError(fieldName))
        {
            ids.Add(validationId);
        }

        return ids.Count == 0 ? null : string.Join(' ', ids);
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
            : uri.Contains("/forgot-password", StringComparison.OrdinalIgnoreCase)
                ? AuthMode.ForgotPassword
                : AuthMode.Login;

        if (authMode == AuthMode.Login)
        {
            ResetLoginModel();
            ApplyLoginValidationStateFromUri(uri);
            _pendingLoginFieldReset = true;
        }
        else if (authMode == AuthMode.ForgotPassword)
        {
            ResetForgotPasswordModel();
        }
    }

    private void ResetLoginModel()
    {
        loginModel.Username = string.Empty;
        loginModel.Pin = string.Empty;
        loginFieldErrors.Clear();
    }

    private void ApplyLoginValidationStateFromUri(string uri)
    {
        if (!TryGetQueryParameter(uri, "loginValidation", out var validationValue))
        {
            return;
        }

        errorMessage = null;
        foreach (var code in validationValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(code, "usernameRequired", StringComparison.OrdinalIgnoreCase))
            {
                loginFieldErrors[nameof(loginModel.Username)] = "Username or email is required.";
            }
            else if (string.Equals(code, "pinRequired", StringComparison.OrdinalIgnoreCase))
            {
                loginFieldErrors[nameof(loginModel.Pin)] = "PIN is required.";
            }
            else if (string.Equals(code, "pinFormat", StringComparison.OrdinalIgnoreCase))
            {
                loginFieldErrors[nameof(loginModel.Pin)] = "PIN must be 4 digits.";
            }
        }
    }

    private static bool TryGetQueryParameter(string uri, string key, out string value)
    {
        value = string.Empty;
        var queryStart = uri.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == uri.Length - 1)
        {
            return false;
        }

        var query = uri[(queryStart + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 0
                || !TryDecodeQueryPart(parts[0], out var decodedKey)
                || !string.Equals(decodedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (parts.Length <= 1)
            {
                value = string.Empty;
                return true;
            }

            return TryDecodeQueryPart(parts[1], out value);
        }

        return false;
    }

    private static bool TryDecodeQueryPart(string rawValue, out string decodedValue)
    {
        decodedValue = string.Empty;

        try
        {
            decodedValue = Uri.UnescapeDataString(rawValue.Replace("+", " ", StringComparison.Ordinal));
            return !string.IsNullOrWhiteSpace(decodedValue);
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            return false;
        }
    }

    private void ResetSignUpModel()
    {
        signUpModel = new SignUpModel();
        InitializeSignUpEditContext();
        signUpFormKey++;
        isPtaFieldsActive = false;
    }

    private void InitializeSignUpEditContext()
    {
        if (signUpEditContext is not null)
        {
            signUpEditContext.OnFieldChanged -= ClearSignUpServerValidationForField;
        }

        signUpEditContext = new EditContext(signUpModel);
        signUpServerValidationMessages = new ValidationMessageStore(signUpEditContext);
        signUpEditContext.OnFieldChanged += ClearSignUpServerValidationForField;
    }

    private void ClearSignUpServerValidationForField(object? sender, FieldChangedEventArgs args)
    {
        signUpServerValidationMessages.Clear(args.FieldIdentifier);
        signUpEditContext.NotifyValidationStateChanged();
    }

    private void ClearSignUpServerValidationMessages()
    {
        signUpServerValidationMessages.Clear();
        signUpEditContext.NotifyValidationStateChanged();
    }

    private void ApplySignUpServerValidation(RegistrationResult result)
    {
        ClearSignUpServerValidationMessages();

        var validationErrors = result.ValidationErrors;
        if (validationErrors is null || validationErrors.Count == 0)
        {
            validationErrors = result.Status switch
            {
                RegistrationStatus.EmailAlreadyExists => new Dictionary<string, string[]>
                {
                    [nameof(signUpModel.Email)] = ["An account with that email already exists."]
                },
                RegistrationStatus.InvalidPin => new Dictionary<string, string[]>
                {
                    [nameof(signUpModel.Pin)] = ["PIN must be exactly 4 digits."]
                },
                RegistrationStatus.ClinicNotFound => new Dictionary<string, string[]>
                {
                    [nameof(signUpModel.ClinicId)] = ["Selected clinic is invalid."]
                },
                _ => null
            };
        }

        if (validationErrors is null)
        {
            return;
        }

        foreach (var (fieldName, messages) in validationErrors)
        {
            var field = new FieldIdentifier(signUpModel, fieldName);
            foreach (var message in messages.Where(static message => !string.IsNullOrWhiteSpace(message)))
            {
                signUpServerValidationMessages.Add(field, message);
            }
        }

        signUpEditContext.NotifyValidationStateChanged();
    }

    private async Task FocusFirstInvalidSignUpFieldAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("ptdocAuth.focusFirstInvalid", "signup-form");
        }
        catch (JSDisconnectedException)
        {
            // The browser is navigating or the circuit was disconnected during validation.
        }
        catch (JSException ex)
        {
            Logger.LogDebug(ex, "Sign-up validation focus helper is unavailable.");
        }
    }

    private void ResetForgotPasswordModel()
    {
        forgotPasswordModel.Contact = string.Empty;
        forgotPasswordModel.Channel = "email";
        forgotPasswordFormKey++;
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

    protected sealed class SignUpModel : IValidatableObject
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

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ClinicId == Guid.Empty)
            {
                yield return new ValidationResult("Clinic is required.", [nameof(ClinicId)]);
            }

            if (!string.IsNullOrWhiteSpace(Pin)
                && !string.IsNullOrWhiteSpace(ConfirmPin)
                && !string.Equals(Pin, ConfirmPin, StringComparison.Ordinal))
            {
                yield return new ValidationResult(
                    "PIN and confirmation PIN do not match.",
                    [nameof(ConfirmPin)]);
            }

            var requiresLicense = string.Equals(RoleKey, "PT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(RoleKey, "PTA", StringComparison.OrdinalIgnoreCase);
            if (!requiresLicense)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(LicenseNumber))
            {
                yield return new ValidationResult(
                    "License number is required for PT/PTA roles.",
                    [nameof(LicenseNumber)]);
            }

            if (string.IsNullOrWhiteSpace(LicenseState))
            {
                yield return new ValidationResult(
                    "License state is required for PT/PTA roles.",
                    [nameof(LicenseState)]);
            }
        }
    }

    protected sealed class ForgotPasswordModel : IValidatableObject
    {
        public string Contact { get; set; } = string.Empty;

        [Required]
        public string Channel { get; set; } = "email";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.Equals(Channel, "sms", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Contact))
                {
                    yield return new ValidationResult("Mobile number is required.", [nameof(Contact)]);
                    yield break;
                }

                var digits = new string(Contact.Where(char.IsDigit).ToArray());
                if (digits.Length is not (10 or 11))
                {
                    yield return new ValidationResult("Enter a valid mobile number.", [nameof(Contact)]);
                }

                yield break;
            }

            if (string.Equals(Channel, "email", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Contact))
                {
                    yield return new ValidationResult("Email address is required.", [nameof(Contact)]);
                    yield break;
                }

                if (!Contact.Contains('@', StringComparison.Ordinal))
                {
                    yield return new ValidationResult("Enter a valid email address.", [nameof(Contact)]);
                }

                yield break;
            }

            yield return new ValidationResult("Choose email or SMS delivery.", [nameof(Channel)]);
        }
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
        Navigation.LocationChanged -= OnLocationChanged;
        ThemeService.OnThemeChanged -= OnThemeChanged;
        signUpEditContext.OnFieldChanged -= ClearSignUpServerValidationForField;
    }
}
