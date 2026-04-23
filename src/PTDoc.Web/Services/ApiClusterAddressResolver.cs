namespace PTDoc.Web.Services;

public sealed class ApiClusterAddressResolver(IConfiguration configuration)
{
    public const string ReverseProxyApiAddressKey = "ReverseProxy:Clusters:apiCluster:Destinations:api:Address";
    public const string DefaultApiClusterAddress = "http://localhost:5170/";

    public Uri ResolveApiClusterAddress()
    {
        var configuredAddress = configuration[ReverseProxyApiAddressKey];
        if (!string.IsNullOrWhiteSpace(configuredAddress)
            && Uri.TryCreate(configuredAddress.Trim(), UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return new Uri(DefaultApiClusterAddress);
    }
}
