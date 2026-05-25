using System.Net;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.UI.Services;
using PatientsPage = PTDoc.UI.Pages.Patients;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class PatientsPageTests : TestContext
{
    [Fact]
    public void InitialLoad_RendersPatientsAndSearchesDefaultDirectory()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PatientListItemResponse
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Alex Patient",
                    FirstName = "Alex",
                    LastName = "Patient",
                    MedicalRecordNumber = "MRN-100",
                    Email = "alex.patient@example.com",
                    Phone = "555-0100",
                    DateOfBirth = new DateTime(1980, 1, 1)
                }
            });

        RegisterServices(patientService.Object, includePatientWrite: true);

        var cut = RenderComponent<PatientsPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alex Patient", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("MRN-100", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("Search by name, MRN, or email", cut.Find("input").GetAttribute("placeholder"));
        });

        patientService.Verify(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void LoadFailure_ShowsInlineRetryWithoutRawExceptionText()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var toastService = new CapturingToastService();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("server=db-prod;password=sensitive"));

        RegisterServices(patientService.Object, includePatientWrite: true, toastService);

        var cut = RenderComponent<PatientsPage>();

        cut.WaitForAssertion(() =>
        {
            var error = cut.Find("[data-testid='patients-error-state']");
            Assert.Contains("Unable to load patients", error.TextContent, StringComparison.Ordinal);
            Assert.Contains("Patients could not be retrieved. Retry when the connection is available.", error.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("server=db-prod", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(["Failed to load patients. Retry when the connection is available."], toastService.ErrorMessages);
        });
    }

    [Fact]
    public void PatientDirectory_EmitsLowercaseAriaBusyTokens()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var pendingLoad = new TaskCompletionSource<IReadOnlyList<PatientListItemResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .Returns(pendingLoad.Task);

        RegisterServices(patientService.Object, includePatientWrite: true);

        var cut = RenderComponent<PatientsPage>();

        Assert.Equal("true", cut.Find(".patients-page-content").GetAttribute("aria-busy"));

        pendingLoad.SetResult(Array.Empty<PatientListItemResponse>());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find(".patients-page-content").GetAttribute("aria-busy"));
        });
    }

    [Fact]
    public void AddPatientAction_RequiresPatientWritePolicy()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());

        RegisterServices(patientService.Object, includePatientWrite: false);

        var readOnly = RenderComponent<PatientsPage>();

        readOnly.WaitForAssertion(() =>
        {
            Assert.Empty(readOnly.FindAll(".global-page-header-primary-action"));
            Assert.Empty(readOnly.FindAll(".modal-container"));
        });
    }

    [Fact]
    public void AddPatientAction_WithPatientWrite_OpensModal()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());

        RegisterServices(patientService.Object, includePatientWrite: true);

        var cut = RenderComponent<PatientsPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".global-page-header-primary-action")));
        cut.Find(".global-page-header-primary-action").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Add New Patient", cut.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(cut.FindAll(".modal-container"));
        });
    }

    [Fact]
    public void FailedCreate_KeepsModalOpenAndShowsSafeFeedback()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var toastService = new CapturingToastService();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("raw backend failure", null, HttpStatusCode.BadRequest));

        RegisterServices(patientService.Object, includePatientWrite: true, toastService);

        var cut = RenderComponent<PatientsPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".global-page-header-primary-action")));
        cut.Find(".global-page-header-primary-action").Click();
        cut.Find("#firstName").Change("Alex");
        cut.Find("#lastName").Change("Patient");
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Change("1990-01-01");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".modal-container"));
            Assert.DoesNotContain("raw backend failure", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Review the patient details and try again.", toastService.ErrorMessages);
        });
    }

    [Fact]
    public void SuccessfulCreate_ClearsActiveSearchAndShowsCreatedPatient()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var createdPatient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Casey",
            LastName = "Created",
            Email = "casey.created@example.com",
            Phone = "555-0102",
            DateOfBirth = new DateTime(1992, 2, 2)
        };
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .Setup(service => service.SearchAsync("archived-filter", 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdPatient);
        var eligiblePatient = new PatientListItemResponse
        {
            Id = patientId,
            DisplayName = "Casey Created",
            FirstName = "Casey",
            LastName = "Created",
            Email = "casey.created@example.com",
            Phone = "555-0102",
            DateOfBirth = createdPatient.DateOfBirth
        };
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .Setup(service => service.SearchEligiblePatientsAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { eligiblePatient });
        intakeService
            .Setup(service => service.EnsureDraftAsync(patientId, It.IsAny<IntakeResponseDraft?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IntakeEnsureDraftResult.Created(new IntakeResponseDraft
            {
                IntakeId = intakeId,
                PatientId = patientId
            }));
        var intakeDeliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        intakeDeliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });

        RegisterServices(
            patientService.Object,
            includePatientWrite: true,
            intakeService: intakeService.Object,
            intakeDeliveryService: intakeDeliveryService.Object);

        var cut = RenderComponent<PatientsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No patients found", cut.Markup, StringComparison.Ordinal));

        cut.Find(".patient-search-input-field").Input("archived-filter");
        cut.WaitForAssertion(() =>
        {
            patientService.Verify(
                service => service.SearchAsync("archived-filter", 200, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.Equal("false", cut.Find(".patients-page-content").GetAttribute("aria-busy"));
        });

        cut.Find(".global-page-header-primary-action").Click();
        cut.WaitForElement("#firstName");
        cut.Find("#firstName").Change("Casey");
        cut.Find("#lastName").Change("Created");
        cut.Find("#email").Change("casey.created@example.com");
        cut.Find("#dob").Change("1992-02-02");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Casey Created", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(string.Empty, cut.Find(".patient-search-input-field").GetAttribute("value"));
            Assert.NotEmpty(cut.FindAll($"button[data-testid='patient-card-{patientId}']"));
            Assert.Contains("Send Intake Form", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value"));
        });
    }

    [Fact]
    public void AddPatientAndSendIntake_CreatesPatientAndOpensPreselectedSendIntake()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var createdPatient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Jamie",
            LastName = "Intake",
            Email = "jamie.intake@example.com",
            Phone = "555-0111",
            DateOfBirth = new DateTime(1986, 3, 4)
        };
        var eligiblePatient = new PatientListItemResponse
        {
            Id = patientId,
            DisplayName = "Jamie Intake",
            FirstName = "Jamie",
            LastName = "Intake",
            Email = "jamie.intake@example.com",
            Phone = "555-0111",
            DateOfBirth = createdPatient.DateOfBirth
        };
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdPatient);
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .Setup(service => service.SearchEligiblePatientsAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { eligiblePatient });
        intakeService
            .Setup(service => service.EnsureDraftAsync(patientId, It.IsAny<IntakeResponseDraft?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IntakeEnsureDraftResult.Created(new IntakeResponseDraft
            {
                IntakeId = intakeId,
                PatientId = patientId
            }));
        var intakeDeliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        intakeDeliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });

        RegisterServices(
            patientService.Object,
            includePatientWrite: true,
            intakeService: intakeService.Object,
            intakeDeliveryService: intakeDeliveryService.Object);

        var cut = RenderComponent<PatientsPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".global-page-header-primary-action")));

        cut.Find(".global-page-header-primary-action").Click();
        cut.WaitForElement("#firstName");
        cut.Find("#firstName").Change("Jamie");
        cut.Find("#lastName").Change("Intake");
        cut.Find("#email").Change("jamie.intake@example.com");
        cut.Find("#phone").Change("555-0111");
        cut.Find("#dob").Change("1986-03-04");
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Patient + Send Intake", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Send Intake Form", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value"));
            Assert.Equal("jamie.intake@example.com", cut.Find("#email").GetAttribute("value"));
            Assert.Equal("555-0111", cut.Find("#phone").GetAttribute("value"));
        });
    }

    [Fact]
    public void PatientCard_ActivatesNativeButtonNavigation()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PatientListItemResponse
                {
                    Id = patientId,
                    DisplayName = "Alex Patient",
                    FirstName = "Alex",
                    LastName = "Patient",
                    DateOfBirth = new DateTime(1980, 1, 1)
                }
            });

        RegisterServices(patientService.Object, includePatientWrite: true);

        var cut = RenderComponent<PatientsPage>();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find($"button[data-testid='patient-card-{patientId}']");
            Assert.Equal("View details for Alex Patient", card.GetAttribute("aria-label"));
        });

        cut.Find($"button[data-testid='patient-card-{patientId}']").Click();

        Assert.EndsWith($"/patient/{patientId}", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleSearchResult_DoesNotOverwriteLatestResult()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientService = new ControllablePatientService();
        RegisterServices(patientService, includePatientWrite: true);

        var cut = RenderComponent<PatientsPage>();

        cut.WaitForAssertion(() => Assert.Contains("No patients found", cut.Markup, StringComparison.Ordinal));

        cut.Find("input").Input("alex");
        cut.WaitForAssertion(() => Assert.Contains(patientService.SearchQueries, query => query == "alex"), TimeSpan.FromSeconds(2));

        cut.Find("input").Input("avery");
        cut.WaitForAssertion(() => Assert.Contains(patientService.SearchQueries, query => query == "avery"), TimeSpan.FromSeconds(2));

        patientService.CompleteSearch("avery", new PatientListItemResponse
        {
            Id = Guid.NewGuid(),
            DisplayName = "Avery Latest",
            FirstName = "Avery",
            LastName = "Latest",
            DateOfBirth = new DateTime(1988, 1, 1)
        });

        cut.WaitForAssertion(() => Assert.Contains("Avery Latest", cut.Markup, StringComparison.Ordinal));

        patientService.CompleteSearch("alex", new PatientListItemResponse
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alex Stale",
            FirstName = "Alex",
            LastName = "Stale",
            DateOfBirth = new DateTime(1980, 1, 1)
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Avery Latest", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Alex Stale", cut.Markup, StringComparison.Ordinal);
        });
    }

    private void RegisterServices(
        IPatientService patientService,
        bool includePatientWrite,
        IToastService? toastService = null,
        IIntakeService? intakeService = null,
        IIntakeDeliveryService? intakeDeliveryService = null)
    {
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        if (includePatientWrite)
        {
            authorization.SetPolicies(AuthorizationPolicies.PatientRead, AuthorizationPolicies.PatientWrite);
        }
        else
        {
            authorization.SetPolicies(AuthorizationPolicies.PatientRead);
        }

        Services.AddSingleton(patientService);
        Services.AddSingleton(intakeService ?? Mock.Of<IIntakeService>());
        Services.AddSingleton(intakeDeliveryService ?? Mock.Of<IIntakeDeliveryService>());
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton(toastService ?? new CapturingToastService());
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

    private sealed class ControllablePatientService : IPatientService
    {
        private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<PatientListItemResponse>>> _pending = new(StringComparer.OrdinalIgnoreCase);

        public List<string?> SearchQueries { get; } = new();

        public Task<PatientResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PatientResponse?>(null);

        public Task<IReadOnlyList<PatientListItemResponse>> SearchAsync(
            string? query = null,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            SearchQueries.Add(query);

            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult<IReadOnlyList<PatientListItemResponse>>(Array.Empty<PatientListItemResponse>());
            }

            var tcs = new TaskCompletionSource<IReadOnlyList<PatientListItemResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[query] = tcs;
            return tcs.Task;
        }

        public void CompleteSearch(string query, params PatientListItemResponse[] patients)
        {
            if (_pending.TryGetValue(query, out var tcs))
            {
                tcs.TrySetResult(patients);
            }
        }

        public Task<PatientResponse> CreateAsync(CreatePatientRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatientResponse?> UpdateAsync(Guid id, UpdatePatientRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<PatientResponse?>(null);

        public Task<IReadOnlyList<PatientDiagnosisDto>?> GetDiagnosesAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatientDiagnosisDto>?>(Array.Empty<PatientDiagnosisDto>());

        public Task<bool> AddDiagnosisAsync(Guid patientId, string icdCode, string description, bool isPrimary, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RemoveDiagnosisAsync(Guid patientId, string icdCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
