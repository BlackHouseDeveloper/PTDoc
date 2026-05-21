using Microsoft.JSInterop;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Connectivity;

[Trait("Category", "CoreCi")]
public sealed class ConnectivityServiceTests
{
    [Fact]
    public async Task JsCallback_UpdatesOnlineStateAndRaisesChangeEvent()
    {
        var service = new ConnectivityService(new FakeJsRuntime(initialOnline: false));
        var observedStates = new List<bool>();
        service.OnConnectivityChanged += observedStates.Add;

        await service.InitializeAsync();
        service.OnConnectivityStatusChanged(true);

        Assert.True(service.IsOnline);
        Assert.Equal([false, true], observedStates);
    }

    private sealed class FakeJsRuntime(bool initialOnline) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "import" && typeof(TValue) == typeof(IJSObjectReference))
            {
                return ValueTask.FromResult((TValue)(object)new FakeConnectivityModule(initialOnline));
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class FakeConnectivityModule(bool initialOnline) : IJSObjectReference
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "getCurrentStatus" && typeof(TValue) == typeof(bool))
            {
                return ValueTask.FromResult((TValue)(object)initialOnline);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
