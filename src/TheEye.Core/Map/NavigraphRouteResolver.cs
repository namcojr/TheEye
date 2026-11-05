using System.Diagnostics;
using Microsoft.Data.Sqlite;
using TheEye.Core.Models;

namespace TheEye.Core.Maps;

public sealed record RouteWaypoint(string Identifier, GeoCoordinate Position);

/// <summary>
/// Reads user-provided Navigraph waypoint and airway data and resolves the fixes
/// in an IVAO route, including intermediate fixes along filed airways.
/// </summary>
public sealed class NavigraphRouteResolver
{
    private static readonly object DefaultLock = new();
    private static NavigraphRouteResolver? _defaultResolver;
    private static DateTime _defaultDatabaseWriteTime;
    private static bool _defaultDatabaseWasChecked;

    private readonly Dictionary<string, List<WaypointCandidate>> _waypoints;
    private readonly Dictionary<string, AirwayCandidate[]> _airways;

    public NavigraphRouteResolver(
        IEnumerable<(string Identifier, GeoCoordinate Position)> waypoints,
        IEnumerable<(string Identifier, string Airway, int Sequence, GeoCoordinate Position)> airwayPoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(airwayPoints);

        _waypoints = new Dictionary<string, List<WaypointCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var waypoint in waypoints)
        {
            AddWaypoint(waypoint.Identifier, waypoint.Position);
        }

        var airwayPointList = airwayPoints.ToArray();
        foreach (var point in airwayPointList)
        {
            AddWaypoint(point.Identifier, point.Position);
        }

        _airways = airwayPointList
            .Where(point => !string.IsNullOrWhiteSpace(point.Airway) && point.Position.IsValid)
            .GroupBy(point => Normalize(point.Airway), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(point => point.Sequence)
                    .Select(point => new AirwayCandidate(
                        Normalize(point.Identifier),
                        point.Sequence,
                        point.Position))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheEyeIVAO",
        "NavData",
        "db.s3db");

    /// <summary>
    /// Returns the cached resolver for the standard user-data path, refreshing it
    /// if the database is installed or replaced while the application is open.
    /// </summary>
    public static NavigraphRouteResolver? GetDefault()
    {
        var path = DefaultDatabasePath;
        if (!File.Exists(path))
        {
            lock (DefaultLock)
            {
                _defaultResolver = null;
                _defaultDatabaseWasChecked = true;
                _defaultDatabaseWriteTime = default;
            }

            return null;
        }

        DateTime writeTime;
        try
        {
            writeTime = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError("Could not inspect Navigraph navdata at '{0}': {1}", path, exception);
            return null;
        }

        lock (DefaultLock)
        {
            if (_defaultDatabaseWasChecked && _defaultDatabaseWriteTime == writeTime)
            {
                return _defaultResolver;
            }

            try
            {
                _defaultResolver = Load(path);
                _defaultDatabaseWriteTime = writeTime;
                _defaultDatabaseWasChecked = true;
                return _defaultResolver;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException or
                    FormatException or InvalidCastException or OverflowException)
            {
                _defaultResolver = null;
                _defaultDatabaseWriteTime = writeTime;
                _defaultDatabaseWasChecked = true;
                Trace.TraceError("Could not load Navigraph navdata at '{0}': {1}", path, exception);
                return null;
            }
        }
    }

    public static NavigraphRouteResolver Load(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("The selected Navigraph navdata database was not found.", databasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var waypoints = new List<(string Identifier, GeoCoordinate Position)>();
        ReadWaypoints(connection, waypoints, "tbl_ea_enroute_waypoints",
            "waypoint_identifier", "waypoint_latitude", "waypoint_longitude");
        ReadWaypoints(connection, waypoints, "tbl_pc_terminal_waypoints",
            "waypoint_identifier", "waypoint_latitude", "waypoint_longitude");
        ReadWaypoints(connection, waypoints, "tbl_d_vhfnavaids",
            "navaid_identifier", "navaid_latitude", "navaid_longitude");
        ReadWaypoints(connection, waypoints, "tbl_db_enroute_ndbnavaids",
            "navaid_identifier", "navaid_latitude", "navaid_longitude");

        var airwayPoints = new List<(string Identifier, string Airway, int Sequence, GeoCoordinate Position)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT waypoint_identifier, route_identifier, route_identifier_postfix,
                       seqno, waypoint_latitude, waypoint_longitude
                FROM tbl_er_enroute_airways
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var identifier = reader.GetString(0);
                var airway = reader.GetString(1) + (reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim());
                if (!TryReadCoordinate(reader, 4, 5, out var position))
                {
                    continue;
                }

                airwayPoints.Add((identifier, airway, reader.GetInt32(3), position));
            }
        }

        if (waypoints.Count == 0 || airwayPoints.Count == 0)
        {
            throw new InvalidDataException("The database does not contain usable enroute waypoint and airway data.");
        }

        return new NavigraphRouteResolver(waypoints, airwayPoints);
    }

    public IReadOnlyList<RouteWaypoint> Resolve(string? route, GeoCoordinate? departure = null)
    {
        // The selected flight asks again every time its position moves on screen,
        // always with the same filing.
        if (_lastResolved is { } last &&
            string.Equals(last.Route, route, StringComparison.Ordinal) &&
            last.Departure == departure)
        {
            return last.Waypoints;
        }

        var waypoints = ResolveRoute(route, departure);
        _lastResolved = new ResolvedRoute(route, departure, waypoints);
        return waypoints;
    }

    private sealed record ResolvedRoute(
        string? Route, GeoCoordinate? Departure, IReadOnlyList<RouteWaypoint> Waypoints);

    private ResolvedRoute? _lastResolved;

    private IReadOnlyList<RouteWaypoint> ResolveRoute(string? route, GeoCoordinate? departure)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return [];
        }

        var tokens = route.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Split('/', 2)[0])
            .Select(Normalize)
            .ToArray();
        var resolved = new List<RouteWaypoint>();
        if (departure is { IsValid: true } start)
        {
            resolved.Add(new RouteWaypoint(string.Empty, start));
        }

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "DCT")
            {
                continue;
            }

            if (TryParseRouteCoordinate(token, out var coordinate))
            {
                AddIfNew(resolved, new RouteWaypoint(token, coordinate));
                continue;
            }

            if (_airways.TryGetValue(token, out var airway))
            {
                var previousIndex = PreviousWaypointToken(tokens, index);
                var nextIndex = NextWaypointToken(tokens, index);
                if (previousIndex < 0 || nextIndex < 0)
                {
                    continue;
                }

                var previousIdentifier = tokens[previousIndex];
                var nextIdentifier = tokens[nextIndex];
                if (TryFindAirwayTransition(
                        airway,
                        previousIdentifier,
                        nextIdentifier,
                        resolved.Count > 0 ? resolved[^1].Position : null,
                        out var entryIndex,
                        out var exitIndex))
                {
                    var entry = airway[entryIndex];
                    if (resolved.Count > 0 && resolved[^1].Identifier.Equals(previousIdentifier, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved[^1] = resolved[^1] with { Position = entry.Position };
                    }
                    else
                    {
                        resolved.Add(new RouteWaypoint(previousIdentifier, entry.Position));
                    }

                    var direction = entryIndex < exitIndex ? 1 : -1;
                    for (var airwayIndex = entryIndex + direction;
                         airwayIndex != exitIndex + direction;
                         airwayIndex += direction)
                    {
                        var point = airway[airwayIndex];
                        AddIfNew(resolved, new RouteWaypoint(point.Identifier, point.Position));
                    }

                    index = nextIndex;
                }

                continue;
            }

            if (!IsWaypointToken(token) || !_waypoints.TryGetValue(token, out var candidates))
            {
                continue;
            }

            var position = resolved.Count > 0 ? resolved[^1].Position : (GeoCoordinate?)null;
            var selected = SelectNearest(candidates, position);
            AddIfNew(resolved, new RouteWaypoint(token, selected.Position));
        }

        return resolved.Where(point => !string.IsNullOrEmpty(point.Identifier)).ToArray();
    }

    private void AddWaypoint(string identifier, GeoCoordinate position)
    {
        var key = Normalize(identifier);
        if (key.Length == 0 || !position.IsValid)
        {
            return;
        }

        if (!_waypoints.TryGetValue(key, out var candidates))
        {
            candidates = [];
            _waypoints.Add(key, candidates);
        }

        if (!candidates.Any(candidate => candidate.Position == position))
        {
            candidates.Add(new WaypointCandidate(position));
        }
    }

    private static void ReadWaypoints(
        SqliteConnection connection,
        ICollection<(string Identifier, GeoCoordinate Position)> result,
        string table,
        string identifierColumn,
        string latitudeColumn,
        string longitudeColumn)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {identifierColumn}, {latitudeColumn}, {longitudeColumn} FROM {table}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0) && TryReadCoordinate(reader, 1, 2, out var position))
            {
                result.Add((reader.GetString(0), position));
            }
        }
    }

    private static bool TryReadCoordinate(
        SqliteDataReader reader,
        int latitudeIndex,
        int longitudeIndex,
        out GeoCoordinate position)
    {
        position = default;
        if (reader.IsDBNull(latitudeIndex) || reader.IsDBNull(longitudeIndex))
        {
            return false;
        }

        position = new GeoCoordinate(
            Convert.ToDouble(reader.GetValue(latitudeIndex), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToDouble(reader.GetValue(longitudeIndex), System.Globalization.CultureInfo.InvariantCulture));
        return position.IsValid;
    }

    private bool TryFindAirwayTransition(
        IReadOnlyList<AirwayCandidate> airway,
        string entryIdentifier,
        string exitIdentifier,
        GeoCoordinate? previousPosition,
        out int entryIndex,
        out int exitIndex)
    {
        var bestScore = double.PositiveInfinity;
        entryIndex = -1;
        exitIndex = -1;

        for (var first = 0; first < airway.Count; first++)
        {
            if (!airway[first].Identifier.Equals(entryIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var second = first + 1; second < airway.Count; second++)
            {
                if (!airway[second].Identifier.Equals(exitIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = previousPosition is { } previous
                    ? RouteBuilder.DistanceNm(previous, airway[first].Position)
                    : 0;
                if (score < bestScore)
                {
                    bestScore = score;
                    entryIndex = first;
                    exitIndex = second;
                }
            }

            for (var second = first - 1; second >= 0; second--)
            {
                if (!airway[second].Identifier.Equals(exitIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = previousPosition is { } previous
                    ? RouteBuilder.DistanceNm(previous, airway[first].Position)
                    : 0;
                if (score < bestScore)
                {
                    bestScore = score;
                    entryIndex = first;
                    exitIndex = second;
                }
            }
        }

        return double.IsFinite(bestScore);
    }

    private int PreviousWaypointToken(IReadOnlyList<string> tokens, int beforeIndex)
    {
        for (var index = beforeIndex - 1; index >= 0; index--)
        {
            if (IsWaypointToken(tokens[index]) && _waypoints.ContainsKey(tokens[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private int NextWaypointToken(IReadOnlyList<string> tokens, int afterIndex)
    {
        for (var index = afterIndex + 1; index < tokens.Count; index++)
        {
            if (_airways.ContainsKey(tokens[index]))
            {
                return -1;
            }

            if (IsWaypointToken(tokens[index]) && _waypoints.ContainsKey(tokens[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static WaypointCandidate SelectNearest(
        IReadOnlyList<WaypointCandidate> candidates,
        GeoCoordinate? reference) =>
        reference is { } position
            ? candidates.MinBy(candidate => RouteBuilder.DistanceNm(position, candidate.Position))!
            : candidates[0];

    private static void AddIfNew(List<RouteWaypoint> waypoints, RouteWaypoint waypoint)
    {
        if (waypoints.Count == 0 ||
            waypoints[^1].Identifier != waypoint.Identifier ||
            waypoints[^1].Position != waypoint.Position)
        {
            waypoints.Add(waypoint);
        }
    }

    private static bool TryParseRouteCoordinate(string token, out GeoCoordinate position)
    {
        position = default;
        if (token.Length != 7 || token[2] is not ('N' or 'S') || token[6] is not ('E' or 'W'))
        {
            return false;
        }

        var latitudeDigits = token.AsSpan(0, 2);
        var longitudeDigits = token.AsSpan(3, 3);
        if (latitudeDigits.ContainsAnyExceptInRange('0', '9') ||
            longitudeDigits.ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        var latitude = int.Parse(latitudeDigits, System.Globalization.CultureInfo.InvariantCulture);
        var longitude = int.Parse(longitudeDigits, System.Globalization.CultureInfo.InvariantCulture);
        position = new GeoCoordinate(
            token[2] == 'S' ? -latitude : latitude,
            token[6] == 'W' ? -longitude : longitude);
        return position.IsValid;
    }

    private static bool IsWaypointToken(string token) =>
        token.Length is >= 2 and <= 7 && token.All(char.IsLetterOrDigit);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed record WaypointCandidate(GeoCoordinate Position);

    private readonly record struct AirwayCandidate(
        string Identifier,
        int Sequence,
        GeoCoordinate Position);
}
