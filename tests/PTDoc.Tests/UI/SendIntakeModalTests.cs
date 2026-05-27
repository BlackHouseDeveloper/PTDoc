using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.UI.Components;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI;

[Trait("Category", "CoreCi")]
public sealed class SendIntakeModalTests : TestContext
{
    [Fact]
    public void InitialPatientId_PreselectsPatientAndLoadsDraftStatus()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var intakeService = CreateIntakeServiceForDraft(patientId, intakeId);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        deliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true,
                LastLinkGeneratedAt = DateTimeOffset.UtcNow
            });
        RegisterServices(intakeService.Object, deliveryService.Object);

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.InitialPatientId, patientId.ToString("D"))
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>
            {
                CreatePatientOption(patientId, email: "alex.patient@example.com", phone: "555-0100")
            }));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value"));
            Assert.Equal("alex.patient@example.com", cut.Find("#email").GetAttribute("value"));
            Assert.Equal("555-0100", cut.Find("#phone").GetAttribute("value"));
            Assert.Contains("Delivery Status", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EmptyEligiblePatients_ShowsStableEmptyStateAndDisablesPatientSelect()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterServices(Mock.Of<IIntakeService>(), Mock.Of<IIntakeDeliveryService>());

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>()));

        var emptyState = cut.Find("[data-testid='send-intake-empty-state']");
        Assert.Contains("No eligible patients are available.", emptyState.TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("#patient-select").HasAttribute("disabled"));
    }

    [Fact]
    public void RerenderWhileOpen_DoesNotRefocusPatientSelect()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetupModalJsModule();
        RegisterServices(Mock.Of<IIntakeService>(), Mock.Of<IIntakeDeliveryService>());

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>
            {
                CreatePatientOption(Guid.NewGuid(), email: "alex.patient@example.com")
            }));

        cut.WaitForAssertion(() => Assert.True(CountFocusInvocations() > 0));
        var focusCount = CountFocusInvocations();

        cut.Find("#email").Change("alex.edited@example.com");

        cut.WaitForAssertion(() => Assert.Equal(focusCount, CountFocusInvocations()));
    }

    [Fact]
    public void ModalJsImport_WhenFirstAttemptIsCanceled_RetriesOnNextRender()
    {
        var jsRuntime = new RetryableModalJsRuntime();
        Services.AddSingleton<IJSRuntime>(jsRuntime);
        RegisterServices(Mock.Of<IIntakeService>(), Mock.Of<IIntakeDeliveryService>());

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>()));

        cut.WaitForAssertion(() => Assert.Equal(1, jsRuntime.ImportAttempts));

        cut.Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, jsRuntime.ImportAttempts);
            Assert.Contains("lockBodyScroll", jsRuntime.ModuleInvocations);
            Assert.Contains("registerEscapeHandler", jsRuntime.ModuleInvocations);
        });
    }

    [Fact]
    public void GenerateLink_RendersInviteLinkAndQrFromExistingDraft()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var intakeService = CreateIntakeServiceForDraft(patientId, intakeId);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        deliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });
        deliveryService
            .Setup(service => service.GetDeliveryBundleAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryBundleResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteUrl = "https://ptdoc.example/intake/access?token=abc",
                QrSvg = "<svg><title>QR</title></svg>",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        RegisterServices(intakeService.Object, deliveryService.Object);

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.InitialPatientId, patientId.ToString("D"))
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>
            {
                CreatePatientOption(patientId, email: "alex.patient@example.com")
            }));

        cut.WaitForAssertion(() => Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value")));
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Generate Link", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("https://ptdoc.example/intake/access?token=abc", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("<title>QR</title>", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LoopbackInviteLinkOnPublicOrigin_ShowsWarningAndDisablesSend()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var intakeService = CreateIntakeServiceForDraft(patientId, intakeId);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        deliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });
        deliveryService
            .Setup(service => service.GetDeliveryBundleAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryBundleResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteUrl = "http://localhost/intake/access?token=abc",
                QrSvg = "<svg></svg>",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        RegisterServices(intakeService.Object, deliveryService.Object);
        Services.AddSingleton<NavigationManager>(new StaticNavigationManager("https://ptdoc.example/"));

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.InitialPatientId, patientId.ToString("D"))
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>
            {
                CreatePatientOption(patientId, email: "alex.patient@example.com")
            }));

        cut.WaitForAssertion(() => Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value")));
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Generate Link", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("This link only works on this computer.", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Send Invite", StringComparison.Ordinal))
                .HasAttribute("disabled"));
        });
    }

    [Fact]
    public void SendInviteFailure_ShowsSafeComponentErrorAndKeepsModalOpen()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var intakeService = CreateIntakeServiceForDraft(patientId, intakeId);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        deliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });
        deliveryService
            .Setup(service => service.GetDeliveryBundleAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryBundleResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteUrl = "https://ptdoc.example/intake/access?token=abc",
                QrSvg = "<svg></svg>",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        deliveryService
            .Setup(service => service.SendInviteAsync(
                It.Is<IntakeSendInviteRequest>(request =>
                    request.IntakeId == intakeId &&
                    request.Channel == IntakeDeliveryChannel.Email &&
                    request.Destination == "alex.patient@example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliverySendResult
            {
                Success = false,
                IntakeId = intakeId,
                PatientId = patientId,
                Channel = IntakeDeliveryChannel.Email,
                ErrorMessage = "Email delivery is not configured."
            });
        RegisterServices(intakeService.Object, deliveryService.Object);

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.InitialPatientId, patientId.ToString("D"))
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>
            {
                CreatePatientOption(patientId, email: "alex.patient@example.com")
            }));

        cut.WaitForAssertion(() => Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value")));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to send the intake email.", cut.Find("[data-testid='send-intake-error']").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Email delivery is not configured.", cut.Find("[data-testid='send-intake-error']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Send Intake Form", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void GenerateLink_WhenDependencyThrowsInvalidOperation_ShowsFallbackError()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var intakeService = CreateIntakeServiceForDraft(patientId, intakeId);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        deliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });
        deliveryService
            .Setup(service => service.GetDeliveryBundleAsync(intakeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Internal patient Jane Smith delivery failure"));
        RegisterServices(intakeService.Object, deliveryService.Object);

        var cut = RenderComponent<SendIntakeModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.InitialPatientId, patientId.ToString("D"))
            .Add(component => component.AvailablePatients, new List<SendIntakeModal.PatientOption>
            {
                CreatePatientOption(patientId, email: "alex.patient@example.com")
            }));

        cut.WaitForAssertion(() => Assert.Equal(patientId.ToString("D"), cut.Find("#patient-select").GetAttribute("value")));
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Generate Link", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var errorText = cut.Find("[data-testid='send-intake-error']").TextContent;
            Assert.Contains("Unable to generate an intake link. Please try again.", errorText, StringComparison.Ordinal);
            Assert.DoesNotContain("Jane Smith", errorText, StringComparison.Ordinal);
        });
    }

    private void RegisterServices(IIntakeService intakeService, IIntakeDeliveryService deliveryService)
    {
        Services.AddSingleton(intakeService);
        Services.AddSingleton(deliveryService);
        Services.AddSingleton<IToastService, CapturingToastService>();
    }

    private static Mock<IIntakeService> CreateIntakeServiceForDraft(Guid patientId, Guid intakeId)
    {
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .Setup(service => service.EnsureDraftAsync(patientId, It.IsAny<IntakeResponseDraft?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IntakeEnsureDraftResult.Existing(new IntakeResponseDraft
            {
                IntakeId = intakeId,
                PatientId = patientId
            }));
        return intakeService;
    }

    private static SendIntakeModal.PatientOption CreatePatientOption(Guid patientId, string? email = null, string? phone = null) => new()
    {
        Id = patientId.ToString("D"),
        Name = "Alex Patient",
        Email = email,
        PhoneNumber = phone
    };

    private int CountFocusInvocations()
        => JSInterop.Invocations.Count(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

    private void SetupModalJsModule()
        => JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");

    private sealed class RetryableModalJsRuntime : IJSRuntime
    {
        private readonly RecordingJsObjectReference module;

        public RetryableModalJsRuntime()
        {
            module = new RecordingJsObjectReference(ModuleInvocations);
        }

        public int ImportAttempts { get; private set; }

        public List<string> ModuleInvocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import")
            {
                ImportAttempts++;
                if (ImportAttempts == 1)
                {
                    throw new OperationCanceledException();
                }

                return ValueTask.FromResult((TValue)(object)module);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class RecordingJsObjectReference(List<string> invocations) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StaticNavigationManager : NavigationManager
    {
        public StaticNavigationManager(string uri)
        {
            Initialize(uri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }

    private sealed class CapturingToastService : IToastService
    {
        public event Action? OnChange;

        public IReadOnlyList<ToastMessage> GetAll() => [];

        public void ShowSuccess(string message, string? title = null) => OnChange?.Invoke();

        public void ShowError(string message, string? title = null) => OnChange?.Invoke();

        public void ShowWarning(string message, string? title = null) => OnChange?.Invoke();

        public void ShowInfo(string message, string? title = null) => OnChange?.Invoke();

        public void Dismiss(Guid id) => OnChange?.Invoke();
    }
}
