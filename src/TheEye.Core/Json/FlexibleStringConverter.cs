using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheEye.Core.Json;

/// <summary>
/// The API uses booleans for military flags on pilots but strings such as
/// "civil"/"both" on airports. This converter accepts both forms.
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
