using System.Text.Json;
using TheEye.Core.Api;
using TheEye.Core.Json;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class IvaoJsonConverterTests
{
    private static readonly JsonSerializerOptions CoordinateOptions = CreateCoordinateOptions();

    [Fact]
    public void CoordinateList_ReadsArraysObjectsAndStrings()
    {
        const string json = """
            [
              [2.5, 48.5],
              { "lat": 49.5, "lng": 3.5 },
              "50.5,4.5",
              null,
              "not-a-coordinate",
              { "lat": 1 }
            ]
            """;

        var coordinates = JsonSerializer.Deserialize<List<IvaoCoordinate>>(json, CoordinateOptions);

        Assert.NotNull(coordinates);
        Assert.Equal(3, coordinates.Count);
        Assert.Equal(new IvaoCoordinate(48.5, 2.5), coordinates[0]);
        Assert.Equal(new IvaoCoordinate(49.5, 3.5), coordinates[1]);
        Assert.Equal(new IvaoCoordinate(50.5, 4.5), coordinates[2]);
    }

    [Fact]
    public void CoordinateList_DisambiguatesLongitudeFirstStrings()
    {
        const string json = """["120.5,30.5"]""";

        var coordinates = JsonSerializer.Deserialize<List<IvaoCoordinate>>(json, CoordinateOptions);

        Assert.NotNull(coordinates);
        Assert.Equal(new IvaoCoordinate(30.5, 120.5), Assert.Single(coordinates));
    }

    [Fact]
    public void CoordinateList_ReturnsEmptyForNullOrNonArray()
    {
        Assert.Empty(JsonSerializer.Deserialize<List<IvaoCoordinate>>("null", CoordinateOptions) ?? []);
        Assert.Empty(JsonSerializer.Deserialize<List<IvaoCoordinate>>("{}", CoordinateOptions) ?? []);
    }

    [Fact]
    public void CoordinateList_WritesLongitudeLatitudeArrays()
    {
        var json = JsonSerializer.Serialize(
            new List<IvaoCoordinate> { new(48.5, 2.5) },
            CoordinateOptions);

        using var document = JsonDocument.Parse(json);
        var pair = document.RootElement[0];
        Assert.Equal(2.5, pair[0].GetDouble());
        Assert.Equal(48.5, pair[1].GetDouble());
    }

    [Fact]
    public void AtcPosition_UsesCoordinateConverterFromAttribute()
    {
        const string json = """{ "regionMapPolygon": [[4.0, 52.0], { "lat": 53.0, "lon": 5.0 }] }""";

        var position = JsonSerializer.Deserialize<IvaoAtcPosition>(json, IvaoJson.Options);

        Assert.NotNull(position);
        Assert.Equal(2, position.RegionMapPolygon.Count);
        Assert.Equal(new IvaoCoordinate(52, 4), position.RegionMapPolygon[0]);
        Assert.Equal(new IvaoCoordinate(53, 5), position.RegionMapPolygon[1]);
    }

    [Fact]
    public void AtcPosition_TreatsNullPolygonAsEmpty()
    {
        const string json = """{ "regionMapPolygon": null }""";

        var position = JsonSerializer.Deserialize<IvaoAtcPosition>(json, IvaoJson.Options);

        Assert.NotNull(position);
        Assert.Empty(position.RegionMapPolygon);
    }

    [Theory]
    [InlineData("true", "military")]
    [InlineData("false", "civil")]
    [InlineData("\"both\"", "both")]
    [InlineData("null", null)]
    public void FlexibleString_ReadsBooleansAndStrings(string json, string? expected)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FlexibleStringConverter());

        Assert.Equal(expected, JsonSerializer.Deserialize<string>(json, options));
    }

    [Fact]
    public void FlexibleString_ReadsAirportMilitaryFlag()
    {
        var airport = JsonSerializer.Deserialize<IvaoAirport>(
            """{ "icao": "EHAM", "military": true }""",
            IvaoJson.Options);

        Assert.NotNull(airport);
        Assert.Equal("EHAM", airport.Icao);
        Assert.Equal("military", airport.Military);
    }

    private static JsonSerializerOptions CreateCoordinateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new IvaoCoordinateListConverter());
        return options;
    }
}
