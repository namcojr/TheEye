using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheEye.Core.Models;

namespace TheEye.Core.Json;

/// <summary>
/// Reads IVAO's two polygon representations:
/// <c>[longitude, latitude]</c> arrays and <c>{ "lat": ..., "lng": ... }</c> objects.
/// </summary>
public sealed class IvaoCoordinateListConverter : JsonConverter<List<IvaoCoordinate>>
{
    // System.Text.Json bypasses converters for null reference values unless the
    // converter opts in. IVAO sends "regionMapPolygon": null for stations
    // without a sector, and downstream code expects an empty list.
    public override bool HandleNull => true;

    public override List<IvaoCoordinate> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return [];
        }

        var result = new List<IvaoCoordinate>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    ReadObject(ref reader, result);
                    break;

                case JsonTokenType.StartArray:
                    ReadArray(ref reader, result);
                    break;

                case JsonTokenType.String:
                    ReadString(reader.GetString(), result);
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<IvaoCoordinate> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var coordinate in value)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(coordinate.Longitude);
            writer.WriteNumberValue(coordinate.Latitude);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static void ReadObject(ref Utf8JsonReader reader, List<IvaoCoordinate> result)
    {
        double? latitude = null;
        double? longitude = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var property = reader.GetString();
            reader.Read();

            if (IsLatitude(property))
            {
                latitude = ReadDouble(ref reader);
            }
            else if (IsLongitude(property))
            {
                longitude = ReadDouble(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        if (latitude is not null && longitude is not null)
        {
            result.Add(new IvaoCoordinate(latitude.Value, longitude.Value));
        }
    }

    private static void ReadArray(ref Utf8JsonReader reader, List<IvaoCoordinate> result)
    {
        var values = new List<double>(2);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var value = ReadDouble(ref reader);
            if (value is not null)
            {
                values.Add(value.Value);
            }
            else
            {
                reader.Skip();
            }
        }

        if (values.Count >= 2)
        {
            result.Add(new IvaoCoordinate(values[1], values[0]));
        }
    }

    private static void ReadString(string? value, List<IvaoCoordinate> result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var first) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var second))
        {
            return;
        }

        // Some older IVAO payloads use "latitude,longitude"; the documented
        // polygon format is "longitude,latitude". The value range is a reliable
        // discriminator for the first component.
        var isLatitudeFirst = Math.Abs(first) <= 90;
        result.Add(isLatitudeFirst
            ? new IvaoCoordinate(first, second)
            : new IvaoCoordinate(second, first));
    }

    private static double? ReadDouble(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String &&
            double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static bool IsLatitude(string? property) =>
        string.Equals(property, "lat", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(property, "latitude", StringComparison.OrdinalIgnoreCase);

    private static bool IsLongitude(string? property) =>
        string.Equals(property, "lng", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(property, "lon", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(property, "long", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(property, "longitude", StringComparison.OrdinalIgnoreCase);
}
