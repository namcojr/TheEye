using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheEye.Core.Json;

/// <summary>
/// Reads a count of seconds the tracker reports as a duration or a time of day.
/// The live payloads send a plain number, but the same members have been seen as
/// strings (<c>"7200"</c>, <c>"7200.0"</c>) and a flight plan must not be thrown
/// away over a formatting detail, so every numeric form is accepted and rounded
/// to whole seconds. Anything else — including a clock such as <c>"02:00"</c>,
/// which IVAO does not send here — reads as "not filed" instead of failing.
/// </summary>
public sealed class SecondsConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    return whole;
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fractional)
                    ? (long)Math.Round(fractional, MidpointRounding.AwayFromZero)
                    : null;

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var seconds)
                    ? seconds
                    : (long)Math.Round(reader.GetDouble(), MidpointRounding.AwayFromZero);

            case JsonTokenType.Null:
                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is { } seconds)
        {
            writer.WriteNumberValue(seconds);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
