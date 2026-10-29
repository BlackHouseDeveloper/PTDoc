using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace PTDoc.Web.Services;

/// <summary>Creates authentication wrappers in the caller's request/circuit scope.</summary>
public sealed class WebScopedHttpClientFactory(
    IServiceProvider services,
    IHttpMessageHandlerFactory transports,
    IOptionsMonitor<HttpClientFactoryOptions> options) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var transport = transports.CreateHandler(name);
        HttpClient client;
        if (name == "ServerAPI")
        {
            var authentication = services.GetRequiredService<ApiAccessTokenForwardingHandler>();
            authentication.InnerHandler = new BorrowedTransport(transport);
            client = new HttpClient(authentication);
        }
        else
        {
            client = new HttpClient(transport, disposeHandler: false);
        }

        foreach (var configure in options.Get(name).HttpClientActions) configure(client);
        return client;
    }

    // Disposing a scoped wrapper must not dispose the shared factory-owned pipeline.
    private sealed class BorrowedTransport(HttpMessageHandler transport) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _invoker = new(transport, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _invoker.SendAsync(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _invoker.Dispose();
            base.Dispose(disposing);
        }
    }
}
