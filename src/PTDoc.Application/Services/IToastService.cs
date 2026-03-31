namespace PTDoc.Application.Services;

/// <summary>
/// Displays transient user-feedback messages (toasts) in the UI.
/// All methods are fire-and-forget; they do not block the caller.
/// </summary>
public interface IToastService
{
    /// <summary>Raised when the toast list changes. Components subscribe to trigger re-renders.</summary>
    event Action? OnChange;

    /// <summary>All active (non-dismissed) toasts, ordered oldest-first.</summary>
    IReadOnlyList<ToastMessage> GetAll();

    void ShowSuccess(string message, string? title = null);
    void ShowError(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowInfo(string message, string? title = null);
    void Dismiss(Guid id);
}

public sealed record ToastMessage(
    Guid Id,
    ToastLevel Level,
    string Message,
    string? Title);

public enum ToastLevel { Success, Error, Warning, Info }
