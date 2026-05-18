using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PTDoc.Api.RequestParsing;

public static class SafeAnonymousJsonBodyReader
{
    public static async Task<JsonDocument?> TryReadObjectAsync(
        HttpContext httpContext,
        string endpointName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Request.ContentLength == 0)
        {
            LogRejectedBody(httpContext, endpointName, "empty_body");
            return null;
        }

        try
        {
            var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            LogRejectedBody(httpContext, endpointName, "non_object_json");
            return null;
        }
        catch (JsonException)
        {
            LogRejectedBody(httpContext, endpointName, "malformed_json");
            return null;
        }
        catch (BadHttpRequestException)
        {
            LogRejectedBody(httpContext, endpointName, "bad_request_body");
            return null;
        }
        catch (ArgumentException)
        {
            LogRejectedBody(httpContext, endpointName, "invalid_request_body");
            return null;
        }
        catch (FormatException)
        {
            LogRejectedBody(httpContext, endpointName, "invalid_request_body_format");
            return null;
        }
        catch (InvalidOperationException)
        {
            LogRejectedBody(httpContext, endpointName, "unreadable_request_body");
            return null;
        }
        catch (InvalidDataException)
        {
            LogRejectedBody(httpContext, endpointName, "invalid_request_body_data");
            return null;
        }
        catch (IOException)
        {
            LogRejectedBody(httpContext, endpointName, "request_body_io_error");
            return null;
        }
    }

    private static void LogRejectedBody(
        HttpContext httpContext,
        string endpointName,
        string failureCategory)
    {
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("PTDoc.Api.AnonymousRequestBody");

        logger.LogWarning(
            "Rejected anonymous JSON request body for {EndpointName}. Path={Path} TraceId={TraceId} ContentType={ContentType} ContentLength={ContentLength} FailureCategory={FailureCategory}",
            SanitizeLogValue(endpointName),
            SanitizeLogValue(httpContext.Request.Path.ToString()),
            SanitizeLogValue(httpContext.TraceIdentifier),
            SanitizeLogValue(httpContext.Request.ContentType),
            httpContext.Request.ContentLength,
            failureCategory);
    }

    private static string? SanitizeLogValue(string? value)
        => value?.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
}
