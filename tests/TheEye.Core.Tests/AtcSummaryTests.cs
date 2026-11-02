using System.Text.Json;
using TheEye.Core.Api;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;

namespace TheEye.Core.Tests;

/// <summary>
/// The ATC summary the tracker answers is what the station card is built from:
/// the published station name, the airport it serves with its city and country,
/// and the frequency. Captured from api.ivao.aero/v2/tracker/now/atc/summary,
/// with the region polygons trimmed.
/// </summary>
public sealed class AtcSummaryTests
{
    private const string AirportStation = """
        {
          "id": 63818174,
          "userId": 788791,
          "callsign": "LROP_TWR",
          "connectionType": "ATC",
          "atcSession": { "frequency": 118.805, "position": "TWR" },
          "atcPosition": {
            "airportId": "LROP",
            "atcCallsign": "Otopeni Tower",
            "military": false,
            "middleIdentifier": null,
            "position": "TWR",
            "composePosition": "LROP_TWR",
            "regionMap": [],
            "regionMapPolygon": [],
            "airport": {
              "icao": "LROP",
              "iata": "OTP",
              "name": "Henri Coanda",
              "countryId": "RO",
              "city": "Bucharest",
              "latitude": 44.5711111111,
              "longitude": 26.085,
              "military": "both"
            }
          },
          "subcenter": null
        }
        """;

    private const string CenterStation = """
        {
          "id": 63819708,
          "userId": 590985,
          "callsign": "LIRR_EW_CTR",
          "connectionType": "ATC",
          "atcSession": { "frequency": 127.125, "position": "CTR" },
          "atcPosition": null,
          "subcenter": {
            "centerId": "LIRR",
            "atcCallsign": "Roma Radar",
            "middleIdentifier": "EW",
            "position": "CTR",
            "composePosition": "LIRR_EW_CTR",
            "military": false,
            "frequency": 127.125,
            "latitude": 40.0,
            "longitude": 10.0,
            "regionMap": [],
            "regionMapPolygon": []
          }
        }
        """;

    private const string ObserverStation = """
        {
          "id": 63830001,
          "userId": 700001,
          "callsign": "LROP_TWR",
          "connectionType": "OBS",
          "atcSession": { "frequency": null, "position": "TWR" },
          "atcPosition": {
            "airportId": "LROP",
            "atcCallsign": "Otopeni Tower",
            "position": "TWR",
            "regionMap": [],
            "regionMapPolygon": [],
            "airport": {
              "icao": "LROP",
              "iata": "OTP",
              "name": "Henri Coanda",
              "countryId": "RO",
              "city": "Bucharest",
              "latitude": 44.5711111111,
              "longitude": 26.085,
              "military": "both"
            }
          },
          "subcenter": null
        }
        """;

    [Fact]
    public void AirportStation_NamesTheAirportTheCardShows()
    {
        var atc = Read(AirportStation);

        Assert.Equal("LROP_TWR", atc.Callsign);
        Assert.Equal("Otopeni Tower", atc.AtcCallsign);
        Assert.Equal("TWR", atc.Facility);
        Assert.Equal("118.805", atc.FrequencyText);
        Assert.Equal("LROP", atc.AirportIcao);
        Assert.Equal("Henri Coanda", atc.AirportName);
        Assert.Equal("Bucharest", atc.City);
        Assert.Equal("RO", atc.Country);
        Assert.Equal("Romania", atc.CountryName);
        Assert.True(atc.HasAirport);
        Assert.Equal("LROP TWR", atc.ScopeLabel);
        Assert.NotNull(atc.Position);
    }

    [Fact]
    public void CenterStation_HasNoCityOrCountryToShow()
    {
        var atc = Read(CenterStation);

        Assert.Equal("LIRR_EW_CTR", atc.Callsign);
        Assert.Equal("Roma Radar", atc.AtcCallsign);
        Assert.Equal("CTR", atc.Facility);
        Assert.Equal("127.125", atc.FrequencyText);
        Assert.False(atc.HasAirport);
        Assert.Equal("—", atc.City);
        Assert.Equal("—", atc.CountryName);
        Assert.Equal("LIRR CTR", atc.ScopeLabel);
    }

    [Fact]
    public void ObserverStation_IsListedAndDrawnAsAnObserver()
    {
        var atc = Read(ObserverStation);

        // IVAO keeps the position of the station being watched, so the connection
        // type is what says the member is only observing.
        Assert.True(atc.IsObserver);
        Assert.Equal("OBS", atc.Facility);
        Assert.Equal("TWR", atc.ReportedFacility);
        Assert.Equal("OBS", atc.PositionText);
        Assert.NotNull(atc.Position);
    }

    [Theory]
    [InlineData("ATC", "EDDB_OBS", "GND")]
    [InlineData("OBS", "EDDB_GND", "GND")]
    [InlineData("ATC", "EDDB_GND", "OBS")]
    public void ObserverStation_IsRecognisedByTheConnectionTheSuffixOrThePosition(
        string connectionType,
        string callsign,
        string position)
    {
        var atc = new IvaoAtc
        {
            Callsign = callsign,
            ConnectionType = connectionType,
            AtcSession = new IvaoAtcSession { Position = position },
            AtcPosition = new IvaoAtcPosition { Position = position }
        };

        Assert.True(atc.IsObserver);
        Assert.Equal("OBS", atc.Facility);
    }

    [Fact]
    public void Controller_IsNotAnObserver()
    {
        var atc = Read(AirportStation);

        Assert.False(atc.IsObserver);
        Assert.False(atc.HasObserverSuffix);
        Assert.Equal("TWR", atc.Facility);
        Assert.Equal("Tower", FacilityPalette.DisplayName(atc.Facility));
        Assert.Equal("Observer", FacilityPalette.DisplayName("OBS"));
        Assert.Equal(AtcMarkerStyles.ObserverFill, AtcMarkerStyles.For("OBS", "LROP_OBS").Fill);
    }

    private static IvaoAtc Read(string json) =>
        JsonSerializer.Deserialize<IvaoAtcSummary>(json, IvaoJson.Options)!.ToAtc();
}
