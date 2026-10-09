using System.Text.Json;

namespace PayerGatewayWebAPI.Helpers;

/// <summary>
/// JSON settings for the plain (non-FHIR) JSON the gateway stores and sends to the EHR:
/// camelCase names, same as the API responses.
/// </summary>
public static class ApiJson
{
    /// <summary>
    /// Web defaults (camelCase names, names read without regard to case, numbers also read from text such as "30"),
    /// plus true / false also read from 1 / 0 and "Y" / "N" (FlexibleBooleanConverter).
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Builds the options once.</summary>
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleBooleanConverter());
        return options;
    }

    /// <summary>Object -> JSON text.</summary>
    public static string Write<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>JSON text -> object; a new empty object when the text is empty.</summary>
    public static T Read<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }
        T? value = JsonSerializer.Deserialize<T>(json, Options);
        if (value == null)
        {
            return new T();
        }
        return value;
    }
}
