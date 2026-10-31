using System.Text.Json;
using TheEye.Core.Api;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class SessionDetailTests
{
    // Trimmed to the members the pilot card reads: identity, ratings, software,
    // simulator, server and the timestamps time online is counted from.
    private const string Payload = """
        {
          "id": 4242,
          "callsign": "TAP123",
          "userId": 123456,
          "connectionType": "PILOT",
          "serverId": "EU-1",
          "time": 3720,
          "softwareTypeId": "ALTITUDE",
          "softwareVersion": "0.4.1",
          "softwareType": { "id": 4, "name": "Altitude" },
          "createdAt": "2026-10-31T10:00:00Z",
          "pilotSession": { "simulatorId": "MSFS", "textureId": 7 },
          "user": {
            "id": 123456,
            "firstName": "Ana",
            "lastName": "Silva",
            "divisionId": "PT",
            "rating": {
              "isAtc": true,
              "isPilot": true,
              "pilotRating": { "id": 3, "name": "Senior Flight Student", "shortName": "FS3" },
              "atcRating": { "id": 2, "name": "Approach Controller", "shortName": "APC" }
            }
          }
        }
        """;

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    // What api.ivao.aero answered for a real pilot session, trimmed to the
    // members the card reads. The software id is a slug rather than a number and
    // the member's name may be withheld; both used to lose the whole record, and
    // every pilot card said "the pilot details could not be loaded".
    private const string LivePilotPayload = """
        {
          "id": 63815993,
          "callsign": "QFA1",
          "userId": 781249,
          "rating": 4,
          "connectionType": "PILOT",
          "serverId": "WS1-EUW2",
          "time": 57767,
          "softwareTypeId": "altitude/win",
          "softwareVersion": "1.14.0.24b",
          "sandbagging": false,
          "isMilitary": false,
          "isWorldTour": false,
          "flightPlans": [
            { "id": 73050831, "arrivalId": "EGLL", "departureId": "YSSY", "aircraftId": "A35K" }
          ],
          "softwareType": { "id": "altitude/win", "name": "Altitude/win" },
          "user": {
            "id": 781249,
            "firstName": null,
            "lastName": null,
            "divisionId": "US",
            "rating": {
              "pilotRatingId": 4,
              "atcRatingId": 3,
              "pilotRating": { "id": 4, "name": "Advanced Flight Student", "shortName": "FS3" },
              "atcRating": { "id": 3, "name": "ATC Trainee", "shortName": "AS2" }
            }
          },
          "createdAt": "2026-10-30T23:57:03.000Z",
          "completedAt": null,
          "updatedAt": "2026-10-31T15:59:38.000Z",
          "isDoubleConnected": false,
          "hideSupervisor": false,
          "pilotSession": { "simulatorId": "MSFS2024", "textureId": 11338 },
          "atcSession": null
        }
        """;

    // And for a real station: the same record, with the controller half instead
    // of the simulator.
    private const string LiveAtcPayload = """
        {
          "id": 63818174,
          "callsign": "LROP_TWR",
          "userId": 788791,
          "rating": 4,
          "connectionType": "ATC",
          "serverId": "WS1-EUW2",
          "time": 23109,
          "softwareTypeId": "aurora/mac",
          "softwareVersion": "1.0.38b",
          "sandbagging": false,
          "isMilitary": false,
          "isWorldTour": false,
          "flightPlans": [],
          "softwareType": { "id": "aurora/mac", "name": "Aurora/mac" },
          "user": {
            "id": 788791,
            "firstName": null,
            "lastName": null,
            "divisionId": "RO",
            "rating": {
              "pilotRatingId": 2,
              "atcRatingId": 4,
              "pilotRating": { "id": 2, "name": "Basic Flight Student", "shortName": "FS1" },
              "atcRating": { "id": 4, "name": "Advanced ATC Trainee", "shortName": "AS3" }
            }
          },
          "createdAt": "2026-10-31T09:34:41.000Z",
          "completedAt": null,
          "updatedAt": "2026-10-31T15:59:43.000Z",
          "isDoubleConnected": false,
          "hideSupervisor": false,
          "pilotSession": null,
          "atcSession": {
            "position": "TWR",
            "frequency": 118.805,
            "createdAt": "2026-10-31T09:34:41.000Z",
            "updatedAt": "2026-10-31T09:34:41.000Z"
          }
        }
        """;

    [Fact]
    public void Deserializes_TheRecordOfASession()
    {
        var session = JsonSerializer.Deserialize<IvaoSessionDetail>(Payload, IvaoJson.Options)!;

        Assert.Equal(4242, session.Id);
        Assert.Equal("TAP123", session.Callsign);
        Assert.Equal(123456, session.UserId);
        Assert.True(session.IsPilot);
        Assert.Equal(3720, session.Time);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero), session.CreatedAt);
        Assert.Equal("Altitude 0.4.1", session.SoftwareText);
        Assert.Equal("MSFS", session.SimulatorText);
        Assert.Equal("EU-1", session.ServerText);
        Assert.Equal(7, session.PilotSession?.TextureId);
    }

    [Fact]
    public void Deserializes_TheMemberBehindTheSession()
    {
        var user = JsonSerializer.Deserialize<IvaoSessionDetail>(Payload, IvaoJson.Options)!.User!;

        Assert.Equal(123456, user.Id);
        Assert.Equal("Ana Silva", user.FullName);
        Assert.Equal("PT", user.DivisionId);
        Assert.Equal("Portugal", user.DivisionName);
        Assert.Equal("https://flagcdn.com/w40/pt.png", user.FlagUrl);
        Assert.Equal("FS3", user.PilotRatingText);
        Assert.Equal("APC", user.AtcRatingText);
        Assert.Equal("https://www.ivao.aero/data/images/ratings/pilot/3.gif", user.PilotBadgeUrl);
        Assert.Equal("https://www.ivao.aero/data/images/ratings/atc/2.gif", user.AtcBadgeUrl);
    }

    [Fact]
    public void FullName_IsAPlaceholderWithoutAName()
    {
        Assert.Equal("IVAO member", new IvaoSessionUser { FirstName = "  ", LastName = null }.FullName);
        Assert.Equal("Ana", new IvaoSessionUser { FirstName = "Ana" }.FullName);
    }

    [Fact]
    public void DivisionName_IsEmptyWithoutADivision()
    {
        var user = new IvaoSessionUser();

        Assert.Equal(string.Empty, user.DivisionName);
        Assert.Null(user.FlagUrl);
        Assert.Null(user.PilotBadgeUrl);
        Assert.Null(user.AtcBadgeUrl);
        Assert.Equal(string.Empty, user.PilotRatingText);
    }

    [Fact]
    public void IsPilot_ReadsTheConnectionType()
    {
        Assert.True(new IvaoSessionDetail { ConnectionType = "pilot" }.IsPilot);
        Assert.False(new IvaoSessionDetail { ConnectionType = "ATC" }.IsPilot);
        Assert.False(new IvaoSessionDetail().IsPilot);
    }

    [Fact]
    public void SoftwareText_CombinesTheNameAndVersion()
    {
        Assert.Equal(
            "Altitude 0.4.1",
            new IvaoSessionDetail { SoftwareType = new IvaoSoftwareSummary { Name = "Altitude" }, SoftwareVersion = "0.4.1" }.SoftwareText);

        Assert.Equal(
            "Altitude",
            new IvaoSessionDetail { SoftwareType = new IvaoSoftwareSummary { Name = "Altitude" } }.SoftwareText);
    }

    [Fact]
    public void SoftwareText_FallsBackToTheSoftwareId()
    {
        Assert.Equal(
            "ALTITUDE 0.4.1",
            new IvaoSessionDetail { SoftwareTypeId = "ALTITUDE", SoftwareVersion = "0.4.1" }.SoftwareText);
    }

    [Fact]
    public void SoftwareText_IsEmptyWithoutAnySoftware() =>
        Assert.Equal(string.Empty, new IvaoSessionDetail().SoftwareText);

    [Fact]
    public void Online_CountsFromTheConnectionTimestamp()
    {
        var session = new IvaoSessionDetail
        {
            CreatedAt = Now.AddHours(-2).AddMinutes(-15),
            Time = 3600
        };

        // The timestamp wins over the reported count, so a cached record keeps
        // counting up instead of freezing at the time of the answer.
        Assert.Equal(TimeSpan.FromMinutes(135), session.Online(Now));
    }

    [Fact]
    public void Online_UsesTheReportedCountWithoutATimestamp() =>
        Assert.Equal(TimeSpan.FromMinutes(62), new IvaoSessionDetail { Time = 3720 }.Online(Now));

    [Fact]
    public void Online_IsZeroWhenTheTimestampIsInTheFuture() =>
        Assert.Equal(TimeSpan.Zero, new IvaoSessionDetail { CreatedAt = Now.AddMinutes(5) }.Online(Now));

    [Fact]
    public void Online_IsNullWhenNothingIsKnown() =>
        Assert.Null(new IvaoSessionDetail().Online(Now));

    [Fact]
    public void Deserializes_TheRecordOfALivePilotSession()
    {
        var session = JsonSerializer.Deserialize<IvaoSessionDetail>(LivePilotPayload, IvaoJson.Options)!;

        Assert.Equal(63815993, session.Id);
        Assert.Equal("QFA1", session.Callsign);
        Assert.Equal(781249, session.UserId);
        Assert.True(session.IsPilot);
        Assert.Equal(57767, session.Time);
        Assert.Equal("Altitude/win 1.14.0.24b", session.SoftwareText);
        Assert.Equal("MSFS2024", session.SimulatorText);
        Assert.Equal("WS1-EUW2", session.ServerText);

        // IVAO withholds the name on some records; the division and the rating
        // are still there, which is what the identity block leans on.
        Assert.Equal("IVAO member", session.User?.FullName);
        Assert.Equal("United States", session.User?.DivisionName);
        Assert.Equal("FS3", session.User?.PilotRatingText);
    }

    [Fact]
    public void Deserializes_TheRecordOfALiveAtcSession()
    {
        var session = JsonSerializer.Deserialize<IvaoSessionDetail>(LiveAtcPayload, IvaoJson.Options)!;

        Assert.Equal("LROP_TWR", session.Callsign);
        Assert.False(session.IsPilot);
        Assert.Equal("Aurora/mac 1.0.38b", session.SoftwareText);
        Assert.Equal(string.Empty, session.SimulatorText);
        Assert.Equal("TWR", session.AtcSession?.Position);
        Assert.Equal(118.805, session.AtcSession!.Frequency!.Value, 3);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 9, 34, 41, TimeSpan.Zero), session.CreatedAt);

        // The controller card names the rating and draws the badge IVAO
        // publishes for the ATC rating id.
        Assert.Equal("AS3", session.User?.AtcRatingText);
        Assert.Equal("Advanced ATC Trainee", session.User?.Rating?.AtcRating?.Name);
        Assert.Equal("https://www.ivao.aero/data/images/ratings/atc/4.gif", session.User?.AtcBadgeUrl);
        Assert.Equal("Romania", session.User?.DivisionName);
        Assert.Equal("https://flagcdn.com/w40/ro.png", session.User?.FlagUrl);
    }
}
