using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.UI.Pages;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class SubscriptionBillingTests : TestContext
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void NoPaymentButton_DoesNotInitializeProvider(bool paid, bool recurring, bool pending, bool enabled)
    {
        using var handler = new BillingHandler { Account = Account(paid, recurring, pending), Enabled = enabled };
        RegisterClient(handler);
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/authorize-net-accept.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<SubscriptionBilling>();
        Assert.Empty(cut.FindAll(".AcceptUI"));
        Assert.DoesNotContain(module.Invocations, call => call.Identifier == "initialize");
    }

    [Fact]
    public void CanceledAccountWithoutSchedule_ShowsSupportRecoveryWithoutPaymentInitialization()
    {
        using var handler = new BillingHandler { Account = Account() with { Status = "canceled" } };
        RegisterClient(handler);
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/authorize-net-accept.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<SubscriptionBilling>();
        Assert.Empty(cut.FindAll(".AcceptUI"));
        Assert.Contains("Contact support", cut.Markup);
        Assert.DoesNotContain(module.Invocations, call => call.Identifier == "initialize");
    }

    [Fact]
    public void PendingOperation_DisablesCancellationButAllowsStatusRefresh()
    {
        using var handler = new BillingHandler { Account = Account(recurring: true, pending: true) with { Status = "active" } };
        RegisterClient(handler);
        var cut = RenderComponent<SubscriptionBilling>();
        Assert.True(cut.Find("input[type=checkbox]").HasAttribute("disabled"));
        cut.Find("input[type=checkbox]").Change(true);
        Assert.True(cut.FindAll("button").Single(button => button.TextContent == "Cancel recurring billing").HasAttribute("disabled"));
        Assert.False(cut.FindAll("button").Single(button => button.TextContent == "Refresh billing status").HasAttribute("disabled"));
    }

    [Fact]
    public void PaymentIsEnabledOnlyAfterRenderedButtonIsInitialized()
    {
        using var handler = new BillingHandler();
        RegisterClient(handler);
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/authorize-net-accept.js");
        module.Mode = JSRuntimeMode.Loose;
        var initialization = module.SetupVoid("initialize", _ => true);
        var cut = RenderComponent<SubscriptionBilling>();
        var button = cut.Find(".AcceptUI");
        cut.WaitForAssertion(() => Assert.Equal(button.Id, Assert.Single(module.Invocations["initialize"]).Arguments[0]));
        cut.Find("input[type=checkbox]").Change(true);
        Assert.True(cut.Find(".AcceptUI").HasAttribute("disabled"));
        initialization.SetVoidResult();
        cut.WaitForAssertion(() => Assert.False(cut.Find(".AcceptUI").HasAttribute("disabled")));
    }

    [Fact]
    public void BillingRefresh_RendersAndInitializesPaymentAfterPendingStateClears()
    {
        using var handler = new BillingHandler { Account = Account(pending: true) };
        RegisterClient(handler);
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/authorize-net-accept.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<SubscriptionBilling>();
        Assert.Empty(cut.FindAll(".AcceptUI"));
        handler.Account = Account();
        cut.Find("button").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".AcceptUI"));
            Assert.Single(module.Invocations.Where(call => call.Identifier == "initialize"));
        });
    }

    private void RegisterClient(BillingHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        Services.AddSingleton(new SubscriptionApiClient(client, client));
    }

    private static SubscriptionAccount Account(bool paid = false, bool recurring = false, bool pending = false)
        => new(Guid.NewGuid(), paid, null, 49, 1, "pending", pending, recurring);

    private sealed class BillingHandler : HttpMessageHandler
    {
        public SubscriptionAccount Account { get; set; } = SubscriptionBillingTests.Account();
        public bool Enabled { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("/offer", StringComparison.Ordinal)
                    ? JsonContent.Create(new SubscriptionOffer(Enabled, 49, 1, "Sandbox", "test-login", "test-client", null))
                    : JsonContent.Create(Account)
            });
    }
}
