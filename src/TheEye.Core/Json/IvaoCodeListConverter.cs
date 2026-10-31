using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheEye.Core.Json;

/// <summary>
/// Reads the equipment lists of a flight plan. The filed plan answers with
/// records — <c>{ "id": "E2", "name": "D-FIS ACARS", "order": 7 }</c> — while
/// the tracker summaries and older revisions send the bare codes, and the whole
/// plan would be lost if the strings the card prints were typed as strings
/// alone. Both forms are accepted, and a member that arrives as anything else
/// reads as "not filed" instead of failing the flight plan around it.
/// </summary>
public sealed class IvaoCodeListConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var codes = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (ReadCode(ref reader) is { Length: > 0 } code)
            {
                codes.Add(code);
            }
        }

        return codes.Count == 0 ? null : [.. codes];
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var code in value)
        {
            writer.WriteStringValue(code);
        }

        writer.WriteEndArray();
    }

    private static string? ReadCode(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString()?.Trim();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var integer)
                    ? integer.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.StartObject:
                // The code the pilot filed, with its description next to it.
                return ReadRecordCode(ref reader);
            default:
                reader.Skip();
                return null;
        }
    }

    private static string? ReadRecordCode(ref Utf8JsonReader reader)
    {
        string? id = null;
        string? name = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var property = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                if (string.Equals(property, "id", StringComparison.OrdinalIgnoreCase))
                {
                    id = reader.GetString();
                }
                else if (string.Equals(property, "name", StringComparison.OrdinalIgnoreCase))
                {
                    name = reader.GetString();
                }
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }

        var code = string.IsNullOrWhiteSpace(id) ? name : id;
        return code?.Trim();
    }
}
