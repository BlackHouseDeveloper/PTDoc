using Microsoft.Extensions.Configuration;
using PTDoc.Infrastructure.Integrations;
using Xunit;

namespace PTDoc.Tests.Integrations;

[Trait("Category", "CoreCi")]
public sealed class IntegrationLaunchTicketStoreTests
{
    [Fact]
    public async Task InMemoryTicket_CanBeConsumedOnlyOnce()
    {
        await using var store = new IntegrationLaunchTicketStore(new ConfigurationBuilder().Build());
        const string token = "0123456789abcdef0123456789abcdef0123456789abcdef";
        const string url = "https://hep.wibbi.com/session/synthetic";

        await store.StoreAsync(token, url, TimeSpan.FromMinutes(1));

        Assert.Equal(url, await store.ConsumeAsync(token));
        Assert.Null(await store.ConsumeAsync(token));
    }

    [Fact]
    public async Task ExpiredInMemoryTicket_IsRejected()
    {
        await using var store = new IntegrationLaunchTicketStore(new ConfigurationBuilder().Build());
        const string token = "abcdef0123456789abcdef0123456789abcdef0123456789";

        await store.StoreAsync(token, "https://hep.wibbi.com/session/synthetic", TimeSpan.Zero);

        Assert.Null(await store.ConsumeAsync(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-launch-token")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdefg")]
    public async Task StoreAsync_RejectsInvalidLaunchToken(string token)
    {
        await using var store = new IntegrationLaunchTicketStore(new ConfigurationBuilder().Build());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StoreAsync(token, "https://hep.wibbi.com/session/synthetic", TimeSpan.FromMinutes(1)));

        Assert.Equal("Integration launch token is invalid.", exception.Message);
    }
}
