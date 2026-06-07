using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.RequestParsing;

public static class ListQueryParameterParser
{
    public static bool TryNormalizeSkip(
        string? skip,
        HttpContext httpContext,
        out int normalizedSkip,
        out IResult? failure)
    {
        normalizedSkip = 0;
        failure = null;

        if (string.IsNullOrWhiteSpace(skip))
        {
            return true;
        }

        if (!int.TryParse(skip, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedSkip))
        {
            failure = CreateBadRequest(httpContext);
            return false;
        }

        normalizedSkip = Math.Max(0, requestedSkip);
        return true;
    }

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
            failure = CreateBadRequest(httpContext);
            return false;
        }

        normalizedTake = requestedTake <= 0
            ? defaultTake
            : Math.Min(requestedTake, maxTake);

        return true;
    }

    private static IResult CreateBadRequest(HttpContext httpContext) =>
        Results.Json(
            new
            {
                error = "The request could not be processed.",
                code = "bad_request",
                correlationId = httpContext.TraceIdentifier
            },
            statusCode: StatusCodes.Status400BadRequest);
}
