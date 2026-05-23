using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Pages.Notes;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class NotesPageTests : TestContext
{
    [Fact]
    public void LoadFailure_ShowsGenericErrorWithoutRawExceptionMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        var toastService = new CapturingToastService();
        Services.AddSingleton<IToastService>(toastService);
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                200,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                0))
            .ThrowsAsync(new InvalidOperationException("sensitive connection string detail"));
        Services.AddSingleton(noteService.Object);

        var authStateTask = Services
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();
        var root = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authStateTask);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NotesPage>(3);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        root.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to load notes", root.Markup, StringComparison.Ordinal);
            Assert.Contains("Notes could not be retrieved. Retry when the connection is available.", root.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive connection string detail", root.Markup, StringComparison.Ordinal);
            Assert.Equal(["Failed to load notes. Retry when the connection is available."], toastService.ErrorMessages);
        });
    }

    [Fact]
    public void AllDates_ShowsOlderNotesReturnedByService()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IToastService>(new CapturingToastService());

        var noteId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var olderDate = DateTime.UtcNow.Date.AddDays(-45);
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                200,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                0))
            .ReturnsAsync(new[]
            {
                new NoteListItemApiResponse
                {
                    Id = noteId,
                    PatientId = patientId,
                    PatientName = "Older Patient",
                    NoteType = NoteType.ProgressNote.ToString(),
                    NoteStatus = NoteStatus.Signed,
                    IsSigned = true,
                    DateOfService = olderDate,
                    LastModifiedUtc = olderDate,
                    CptCodesJson = "[]"
                }
            });
        Services.AddSingleton(noteService.Object);

        var authStateTask = Services
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();
        var root = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authStateTask);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NotesPage>(3);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        root.WaitForAssertion(() =>
        {
            Assert.Contains("Older Patient", root.Markup, StringComparison.Ordinal);
            Assert.Contains("Notes", root.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class CapturingToastService : IToastService
    {
        private readonly List<string> _errorMessages = new();

        public event Action? OnChange;

        public IReadOnlyList<string> ErrorMessages => _errorMessages;

        public IReadOnlyList<ToastMessage> GetAll() => [];

        public void ShowSuccess(string message, string? title = null) => OnChange?.Invoke();

        public void ShowError(string message, string? title = null)
        {
            _errorMessages.Add(message);
            OnChange?.Invoke();
        }

        public void ShowWarning(string message, string? title = null) => OnChange?.Invoke();

        public void ShowInfo(string message, string? title = null) => OnChange?.Invoke();

        public void Dismiss(Guid id) => OnChange?.Invoke();
    }
}
