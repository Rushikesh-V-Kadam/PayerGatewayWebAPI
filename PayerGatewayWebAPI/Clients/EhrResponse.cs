using System.Text.Json;
using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;

namespace PayerGatewayWebAPI.Clients;

// CLIENT-EHR: every answer of the client's EHR data API has this format:
//   { "<EntityName>": <row, list of rows, or null>, "TOKEN_EXPIRED": "", "ERROR_MESSAGE": "" }
/// <summary>
/// Reads the client EHR's answer format. The entity is the one property that is not TOKEN_EXPIRED or ERROR_MESSAGE,
/// whatever its name (PayerDetail, DtrLinkDetail, CdexTaskList ...), so a different entity name does not break the
/// gateway. A body without TOKEN_EXPIRED and ERROR_MESSAGE is taken as the data itself.
///   TOKEN_EXPIRED or ERROR_MESSAGE filled -> EhrException (503 for the caller; the text is in the log)
///   entity null                           -> null (no such row)
/// </summary>
public static class EhrResponse
{
    /// <summary>Filled when the EHR's token expired.</summary>
    public const string TokenExpiredName = "TOKEN_EXPIRED";

    /// <summary>Filled when the call failed.</summary>
    public const string ErrorMessageName = "ERROR_MESSAGE";

    /// <summary>The entity of the answer (null when empty or null). Throws EhrException when the answer reports an error.</summary>
    public static JsonNode? Unwrap(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        JsonNode? body;
        try
        {
            body = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new EhrException(label + " returned a body that is not JSON: " + ex.Message, ex);
        }

        JsonObject? answer = body as JsonObject;
        if (answer == null || !IsWrapper(answer))
        {
            return body;   // not the client's format: the body is the data itself
        }

        // CLIENT-EHR: TOKEN_EXPIRED and ERROR_MESSAGE are empty texts when the call worked.
        // TODO: confirm with the EHR team which values TOKEN_EXPIRED holds; for now any text in it is an error.
        string? tokenExpired = ReadText(answer, TokenExpiredName);
        if (!string.IsNullOrWhiteSpace(tokenExpired))
        {
            throw new EhrException(label + " answered TOKEN_EXPIRED = '" + TextHelper.Shorten(tokenExpired, 100) + "'.");
        }
        string? errorMessage = ReadText(answer, ErrorMessageName);
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new EhrException(label + " answered with an error: " + TextHelper.Shorten(errorMessage, 300));
        }

        foreach (KeyValuePair<string, JsonNode?> property in answer)
        {
            if (IsStatusName(property.Key))
            {
                continue;
            }
            return property.Value;   // the first (and only) entity property
        }
        return null;
    }

    /// <summary>True when the object has TOKEN_EXPIRED or ERROR_MESSAGE (upper / lower case does not matter).</summary>
    private static bool IsWrapper(JsonObject answer)
    {
        foreach (KeyValuePair<string, JsonNode?> property in answer)
        {
            if (IsStatusName(property.Key))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True for TOKEN_EXPIRED and ERROR_MESSAGE.</summary>
    private static bool IsStatusName(string name)
    {
        return string.Equals(name, TokenExpiredName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, ErrorMessageName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The value of TOKEN_EXPIRED / ERROR_MESSAGE as text (a non-text value is written as JSON), or null.</summary>
    private static string? ReadText(JsonObject answer, string name)
    {
        foreach (KeyValuePair<string, JsonNode?> property in answer)
        {
            if (!string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase) || property.Value == null)
            {
                continue;
            }
            JsonValue? value = property.Value as JsonValue;
            string? text;
            if (value != null && value.TryGetValue<string>(out text))
            {
                return text;
            }
            return property.Value.ToJsonString();
        }
        return null;
    }
}
