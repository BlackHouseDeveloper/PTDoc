using System.Net;
using System.Text.Json;

namespace PTDoc.UI.Services;

internal static class ApiErrorReader
{
    public static async Task<string?> ReadMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadMessage(payload, response.StatusCode);
    }

    public static string? ReadMessage(string? payload, HttpStatusCode? statusCode = null)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            var parsed = TryReadMessage(payload);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => "The request was rejected by the API.",
            HttpStatusCode.Unauthorized => "Your session is missing or expired. Sign in again.",
            HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
            HttpStatusCode.NotFound => "The requested record could not be found.",
            HttpStatusCode.Conflict => "The request conflicted with the current server state.",
            HttpStatusCode.UnprocessableEntity => "The submitted data failed server validation.",
            _ => null
        };
    }

    private static string? TryReadMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return ReadElement(document.RootElement);
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(payload) ? null : payload.Trim();
        }
    }

    private static string? ReadElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propertyName in new[] { "error", "message", "detail", "title" })
                {
                    if (element.TryGetProperty(propertyName, out var property)
                        && property.ValueKind == JsonValueKind.String)
                    {
                        var value = property.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value.Trim();
                        }
                    }
                }

                if (element.TryGetProperty("errors", out var errors))
                {
                    var validationMessage = ReadValidationErrors(errors);
                    if (!string.IsNullOrWhiteSpace(validationMessage))
                    {
                        return validationMessage;
                    }
                }

                if (element.TryGetProperty("validationFailures", out var validationFailures))
                {
                    var failureMessage = ReadValidationErrors(validationFailures);
                    if (!string.IsNullOrWhiteSpace(failureMessage))
                    {
                        return failureMessage;
                    }
                }

                break;

            case JsonValueKind.Array:
                var items = element
                    .EnumerateArray()
                    .Select(ReadElement)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();

                if (items.Length > 0)
                {
                    return string.Join(" ", items);
                }

                break;

            case JsonValueKind.String:
                return element.GetString()?.Trim();
        }

        return null;
    }

    private static string? ReadValidationErrors(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element
                .EnumerateArray()
                .Select(ReadElement)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();

            return items.Length > 0 ? string.Join(" ", items) : null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var messages = new List<string>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        messages.Add(value.Trim());
                    }
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value.Trim());
                }
            }
        }

        return messages.Count > 0 ? string.Join(" ", messages) : null;
    }
}
