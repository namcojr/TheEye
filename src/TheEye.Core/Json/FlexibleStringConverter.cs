using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheEye.Core.Json;

/// <summary>
/// The API uses booleans for military flags on pilots but strings such as
/// "civil"/"both" on airports, and it reports some ids as slugs ("altitude/win")
/// where older records carried a number. This converter accepts every form and
/// hands the value back as text, so a payload is never rejected over a field the
/// card only prints.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
                return "military";
            case JsonTokenType.False:
                return "civil";
            case JsonTokenType.Number:
                // A slug in one answer and an id in the next: the text form is
                // what the card and the search field both read.
                return reader.TryGetInt64(out var integer)
                    ? integer.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
