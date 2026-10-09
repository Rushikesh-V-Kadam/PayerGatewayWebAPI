using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Helpers;

/// <summary>
/// Reads a true / false value of a row from the EHR data API (used for every row type through ApiJson.Options,
/// for example adaptive / payerFinished of a DTR form). The agreed format is the JSON value true or false.
/// A yes / no column is NUMBER(1) in Oracle, so an API on top of it can easily return 1 / 0 (or "Y" / "N") instead;
/// those are read as well, so one such value does not make the whole answer unreadable.
/// (Payer rows are read value by value in EhrApiPayerRepository, which is more forgiving still.)
/// Accepted: true, false, 1, 0, "true", "false", "1", "0", "Y", "N", "yes", "no" (upper / lower case does not matter).
/// Anything else is an error (JsonException), as before. Writing is unchanged: always the JSON value true or false.
/// </summary>
public class FlexibleBooleanConverter : JsonConverter<bool>
{
    /// <summary>Reads one true / false value.</summary>
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            return true;
        }
        if (reader.TokenType == JsonTokenType.False)
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            long number;
            if (reader.TryGetInt64(out number) && (number == 0 || number == 1))
            {
                return number == 1;
            }
            throw new JsonException("A true / false value was expected, but the number is not 0 or 1.");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string text = (reader.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "true" || text == "1" || text == "y" || text == "yes")
            {
                return true;
            }
            if (text == "false" || text == "0" || text == "n" || text == "no")
            {
                return false;
            }
            throw new JsonException("A true / false value was expected, but the text is '" + text + "'.");
        }

        throw new JsonException("A true / false value was expected.");
    }

    /// <summary>Writes the JSON value true or false.</summary>
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
