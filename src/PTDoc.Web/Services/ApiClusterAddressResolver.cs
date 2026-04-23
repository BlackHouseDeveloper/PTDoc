namespace PTDoc.Web.Services;

public sealed class ApiClusterAddressResolver(IConfiguration configuration)
{
    public const string ReverseProxyApiAddressKey = "ReverseProxy:Clusters:apiCluster:Destinations:api:Address";
    public const string DefaultApiClusterAddress = "http://localhost:5170/";

    public Uri ResolveApiClusterAddress()
    {
        var configuredAddress = configuration[ReverseProxyApiAddressKey];
        if (Uri.TryCreate(configuredAddress, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return new Uri(DefaultApiClusterAddress);
    }
}
