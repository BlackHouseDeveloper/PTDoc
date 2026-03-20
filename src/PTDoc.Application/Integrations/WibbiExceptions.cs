namespace PTDoc.Application.Integrations;

public sealed class WibbiAuthenticationException : Exception
{
    public WibbiAuthenticationException(string message, string operation, int? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        UpstreamStatusCode = upstreamStatusCode;
    }

    public string Operation { get; }

    public int? UpstreamStatusCode { get; }
}

public sealed class WibbiConfigurationException : Exception
{
    public WibbiConfigurationException(string message, string operation)
        : base(message)
    {
        Operation = operation;
    }

    public string Operation { get; }
}

public sealed class WibbiUnsafeLaunchUrlException : Exception
{
    public WibbiUnsafeLaunchUrlException(string message, string operation, IReadOnlyCollection<string> blockedParameters)
        : base(message)
    {
        Operation = operation;
        BlockedParameters = blockedParameters;
    }

    public string Operation { get; }

    public IReadOnlyCollection<string> BlockedParameters { get; }
}
