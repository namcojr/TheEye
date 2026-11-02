using Microsoft.Data.Sqlite;
using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class NavigraphRouteResolverTests
{
    [Fact]
    public void Resolve_UsesAirwaySequenceToSelectDuplicateFixesAndIncludesIntermediateFixes()
    {
        var resolver = new NavigraphRouteResolver(
            [
                ("FIXA", new GeoCoordinate(50, 0)),
                ("FIXA", new GeoCoordinate(50, 10)),
                ("FIXB", new GeoCoordinate(51, 1)),
                ("FIXB", new GeoCoordinate(51, 11)),
                ("FIXC", new GeoCoordinate(52, 12))
            ],
            [
                ("FIXA", "Q41", 10, new GeoCoordinate(50, 0)),
                ("MID", "Q41", 20, new GeoCoordinate(50.5, 0.5)),
                ("FIXB", "Q41", 30, new GeoCoordinate(51, 1)),
                ("FIXA", "Q41", 110, new GeoCoordinate(50, 10)),
                ("AIRWAYMID", "Q41", 120, new GeoCoordinate(50.5, 10.5)),
                ("FIXB", "Q41", 130, new GeoCoordinate(51, 11))
            ]);

        var route = resolver.Resolve(
            "FIXA Q41 FIXB DCT FIXC",
            departure: new GeoCoordinate(50, 10));

        Assert.Equal(["FIXA", "AIRWAYMID", "FIXB", "FIXC"], route.Select(waypoint => waypoint.Identifier));
        Assert.Equal(new GeoCoordinate(50, 10), route[0].Position);
        Assert.Equal(new GeoCoordinate(50.5, 10.5), route[1].Position);
        Assert.Equal(new GeoCoordinate(51, 11), route[2].Position);
        Assert.Equal(new GeoCoordinate(52, 12), route[3].Position);
    }

    [Fact]
    public void Resolve_HandlesReverseAirwayTravelAndAirwayPostfix()
    {
        var resolver = new NavigraphRouteResolver(
            [
                ("ENTRY", new GeoCoordinate(10, 2)),
                ("EXIT", new GeoCoordinate(10, 0))
            ],
            [
                ("EXIT", "UL9A", 10, new GeoCoordinate(10, 0)),
                ("MID", "UL9A", 20, new GeoCoordinate(10, 1)),
                ("ENTRY", "UL9A", 30, new GeoCoordinate(10, 2))
            ]);

        var route = resolver.Resolve("ENTRY UL9A EXIT");

        Assert.Equal(["ENTRY", "MID", "EXIT"], route.Select(waypoint => waypoint.Identifier));
        Assert.Equal(new GeoCoordinate(10, 1), route[1].Position);
        Assert.Equal(new GeoCoordinate(10, 0), route[2].Position);
    }

    [Fact]
    public void Resolve_UsesAirwayFixesWhenTheyAreMissingFromWaypointTables()
    {
        var resolver = new NavigraphRouteResolver(
            [],
            [
                ("ENTRY", "Q41", 10, new GeoCoordinate(50, 0)),
                ("UNFILED", "Q41", 20, new GeoCoordinate(50.5, 0.5)),
                ("EXIT", "Q41", 30, new GeoCoordinate(51, 1))
            ]);

        var route = resolver.Resolve("ENTRY Q41 EXIT");

        Assert.Equal(["ENTRY", "UNFILED", "EXIT"], route.Select(waypoint => waypoint.Identifier));
        Assert.Equal(new GeoCoordinate(50, 0), route[0].Position);
        Assert.Equal(new GeoCoordinate(50.5, 0.5), route[1].Position);
        Assert.Equal(new GeoCoordinate(51, 1), route[2].Position);
    }

    [Fact]
    public void Resolve_StripsSpeedAndLevelSuffixesAttachedToWaypointTokens()
    {
        var resolver = new NavigraphRouteResolver(
            [
                ("ENTRY", new GeoCoordinate(10, 0)),
                ("EXIT", new GeoCoordinate(10, 1))
            ],
            [
                ("ENTRY", "Q41", 10, new GeoCoordinate(10, 0)),
                ("EXIT", "Q41", 20, new GeoCoordinate(10, 1))
            ]);

        var route = resolver.Resolve("ENTRY Q41 EXIT/K0930S1040");

        Assert.Equal(["ENTRY", "EXIT"], route.Select(waypoint => waypoint.Identifier));
        Assert.Equal(new GeoCoordinate(10, 1), route[1].Position);
    }

    [Fact]
    public void Load_ReadsRouteTablesFromAReadOnlyNavigraphDatabase()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("db.s3db");
        CreateDatabase(path);

        var resolver = NavigraphRouteResolver.Load(path);

        var route = resolver.Resolve("FIXA Q41 FIXB", new GeoCoordinate(50, 0));
        Assert.Equal(["FIXA", "MID", "FIXB"], route.Select(waypoint => waypoint.Identifier));
        Assert.Equal(new GeoCoordinate(50.5, 0.5), route[1].Position);
        Assert.Equal(new GeoCoordinate(51, 1), route[2].Position);
    }

    private static void CreateDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE tbl_ea_enroute_waypoints (
                waypoint_identifier TEXT, waypoint_latitude REAL, waypoint_longitude REAL
            );
            CREATE TABLE tbl_pc_terminal_waypoints (
                waypoint_identifier TEXT, waypoint_latitude REAL, waypoint_longitude REAL
            );
            CREATE TABLE tbl_d_vhfnavaids (
                navaid_identifier TEXT, navaid_latitude REAL, navaid_longitude REAL
            );
            CREATE TABLE tbl_db_enroute_ndbnavaids (
                navaid_identifier TEXT, navaid_latitude REAL, navaid_longitude REAL
            );
            CREATE TABLE tbl_er_enroute_airways (
                waypoint_identifier TEXT, route_identifier TEXT, route_identifier_postfix TEXT,
                seqno INTEGER, waypoint_latitude REAL, waypoint_longitude REAL
            );
            INSERT INTO tbl_ea_enroute_waypoints VALUES ('FIXA', 50, 0), ('FIXB', 51, 1);
            INSERT INTO tbl_er_enroute_airways VALUES
                ('FIXA', 'Q41', NULL, 10, 50, 0),
                ('MID', 'Q41', NULL, 20, 50.5, 0.5),
                ('FIXB', 'Q41', NULL, 30, 51, 1);
            """;
        command.ExecuteNonQuery();
    }
}
