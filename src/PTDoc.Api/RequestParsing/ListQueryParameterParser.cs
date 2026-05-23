using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.RequestParsing;

public static class ListQueryParameterParser
{
    public static bool TryNormalizeTake(
        string? take,
        int defaultTake,
        int maxTake,
        HttpContext httpContext,
        out int normalizedTake,
        out IResult? failure)
    {
        normalizedTake = defaultTake;
        failure = null;

        if (string.IsNullOrWhiteSpace(take))
        {
            return true;
        }

        if (!int.TryParse(take, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedTake))
        {
            failure = Results.Json(
                new
                {
                    error = "The request could not be processed.",
                    code = "bad_request",
                    correlationId = httpContext.TraceIdentifier
                },
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        normalizedTake = requestedTake <= 0
            ? defaultTake
            : Math.Min(requestedTake, maxTake);

        return true;
    }
}
