using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

/// <summary>
/// Scoped toast notification service. Components in the same Blazor circuit
/// share this instance and subscribe to <see cref="OnChange"/> to re-render.
/// Toasts auto-dismiss after <see cref="AutoDismissMs"/> milliseconds unless
/// they are error-level, which require explicit user dismissal.
/// </summary>
public sealed class ToastService : IToastService
{
    private const int AutoDismissMs = 4000;
    private readonly List<ToastMessage> _toasts = new();

    public event Action? OnChange;

    public IReadOnlyList<ToastMessage> GetAll() => _toasts.AsReadOnly();

    public void ShowSuccess(string message, string? title = null) =>
        Add(ToastLevel.Success, message, title);

    public void ShowError(string message, string? title = null) =>
        Add(ToastLevel.Error, message, title);

    public void ShowWarning(string message, string? title = null) =>
        Add(ToastLevel.Warning, message, title);

    public void ShowInfo(string message, string? title = null) =>
        Add(ToastLevel.Info, message, title);

    public void Dismiss(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast is null) return;
        _toasts.Remove(toast);
        OnChange?.Invoke();
    }

    private void Add(ToastLevel level, string message, string? title)
    {
        var toast = new ToastMessage(Guid.NewGuid(), level, message, title);
        _toasts.Add(toast);
        OnChange?.Invoke();

        if (level != ToastLevel.Error)
            _ = AutoDismissAfterAsync(toast.Id, AutoDismissMs);
    }

    private async Task AutoDismissAfterAsync(Guid id, int delayMs)
    {
        await Task.Delay(delayMs);
        Dismiss(id);
    }
}
