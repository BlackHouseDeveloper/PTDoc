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
                51,
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
                51,
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

    [Fact]
    public void LoadMore_AppendsNextNotesPage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IToastService>(new CapturingToastService());

        var patientId = Guid.NewGuid();
        var firstPage = Enumerable.Range(1, 51)
            .Select(index => new NoteListItemApiResponse
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                PatientName = $"Patient {index:00}",
                NoteType = NoteType.ProgressNote.ToString(),
                NoteStatus = NoteStatus.Draft,
                IsSigned = false,
                DateOfService = DateTime.UtcNow.Date.AddMinutes(-index),
                LastModifiedUtc = DateTime.UtcNow.Date.AddMinutes(-index),
                CptCodesJson = "[]"
            })
            .ToArray();

        var secondPage = new[]
        {
            new NoteListItemApiResponse
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                PatientName = "Patient 52",
                NoteType = NoteType.ProgressNote.ToString(),
                NoteStatus = NoteStatus.Draft,
                IsSigned = false,
                DateOfService = DateTime.UtcNow.Date.AddMinutes(-52),
                LastModifiedUtc = DateTime.UtcNow.Date.AddMinutes(-52),
                CptCodesJson = "[]"
            }
        };

        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                51,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                0))
            .ReturnsAsync(firstPage);
        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                51,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                50))
            .ReturnsAsync(secondPage);
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

        root.WaitForElement(".notes-recent-load-more");
        Assert.Contains("Patient 50", root.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Patient 51", root.Markup, StringComparison.Ordinal);

        root.Find(".notes-recent-load-more").Click();

        root.WaitForAssertion(() =>
        {
            Assert.Contains("Patient 52", root.Markup, StringComparison.Ordinal);
            Assert.Empty(root.FindAll(".notes-recent-load-more"));
        });
    }

    [Fact]
    public void QueryFilters_AreAppliedToInitialNotesLoad()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IToastService>(new CapturingToastService());

        DateTime? capturedDateRangeStart = null;
        DateTime? capturedDateRangeEnd = null;
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                "Unsigned",
                51,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                It.Is<DateTime?>(value => value.HasValue),
                It.Is<DateTime?>(value => value.HasValue),
                0))
            .Callback<Guid?, string?, string?, int, string?, string?, CancellationToken, string?, DateTime?, DateTime?, int>(
                (_, _, _, _, _, _, _, _, dateRangeStart, dateRangeEnd, _) =>
                {
                    capturedDateRangeStart = dateRangeStart;
                    capturedDateRangeEnd = dateRangeEnd;
                })
            .ReturnsAsync(new[]
            {
                new NoteListItemApiResponse
                {
                    Id = Guid.NewGuid(),
                    PatientId = Guid.NewGuid(),
                    PatientName = "Unsigned Patient",
                    NoteType = NoteType.Daily.ToString(),
                    NoteStatus = NoteStatus.Draft,
                    IsSigned = false,
                    DateOfService = DateTime.UtcNow.Date,
                    LastModifiedUtc = DateTime.UtcNow.Date,
                    CptCodesJson = "[]"
                }
            });
        Services.AddSingleton(noteService.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/notes?status=Unsigned&dateRange=today");

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
            Assert.Contains("Unsigned Patient", root.Markup, StringComparison.Ordinal);
            Assert.True(capturedDateRangeStart.HasValue);
            Assert.Equal(capturedDateRangeStart, capturedDateRangeEnd);
        });
    }

    [Fact]
    public void MalformedQueryString_DoesNotCrashInitialNotesLoad()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IToastService>(new CapturingToastService());

        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                51,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                0))
            .ReturnsAsync(Array.Empty<NoteListItemApiResponse>());
        Services.AddSingleton(noteService.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/notes?status=%ZZ&dateRange=today");

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
