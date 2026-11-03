using System.Globalization;
using System.Text;
using TheEye.Core.Models;

namespace TheEye.Core.Reference;

/// <summary>
/// One runway of the bundled airport table: the two thresholds and how wide the
/// pavement is. Both thresholds are real coordinates, so the map can draw the
/// strip in the right direction instead of guessing a heading.
/// </summary>
public sealed record AirportRunway(GeoCoordinate Threshold, GeoCoordinate End, double WidthMetres)
{
    /// <summary>Middle of the runway, i.e. where the strip is widest on screen.</summary>
    public GeoCoordinate Midpoint => new(
        (Threshold.Latitude + End.Latitude) / 2,
        (Threshold.Longitude + End.Longitude) / 2);

    /// <summary>
    /// Length in metres, from an equirectangular approximation. Good to a few
    /// tenths of a percent at runway scale, which is all a label needs.
    /// </summary>
    public double LengthMetres
    {
        get
        {
            const double MetresPerDegree = 111_320;
            var midLatitude = Midpoint.Latitude * Math.PI / 180;
            var northSouth = (End.Latitude - Threshold.Latitude) * MetresPerDegree;
            var eastWest = (End.Longitude - Threshold.Longitude) * MetresPerDegree * Math.Cos(midLatitude);
            return Math.Sqrt(northSouth * northSouth + eastWest * eastWest);
        }
    }

    public double TrueHeadingDegrees
    {
        get
        {
            var startLatitude = Threshold.Latitude * Math.PI / 180;
            var endLatitude = End.Latitude * Math.PI / 180;
            var longitudeDelta = (End.Longitude - Threshold.Longitude) * Math.PI / 180;
            var y = Math.Sin(longitudeDelta) * Math.Cos(endLatitude);
            var x = Math.Cos(startLatitude) * Math.Sin(endLatitude) -
                    Math.Sin(startLatitude) * Math.Cos(endLatitude) * Math.Cos(longitudeDelta);
            return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        }
    }

    public string RunwayDesignations
    {
        get
        {
            var first = (int)Math.Round(TrueHeadingDegrees / 10, MidpointRounding.AwayFromZero) % 36;
            if (first == 0)
            {
                first = 36;
            }

            var reciprocal = first <= 18 ? first + 18 : first - 18;
            return $"{first:00}/{reciprocal:00}";
        }
    }

    public string LengthText => $"{LengthMetres:N0} m  ·  {LengthMetres * 3.28084:N0} ft";

    public string WidthText => $"{WidthMetres:0} m";

    public string HeadingText => $"{TrueHeadingDegrees:000}°T";
}

/// <summary>
/// An airport of the bundled table, with its runways when the source knew them.
/// </summary>
public sealed record AirportRecord(
    string Icao,
    GeoCoordinate Position,
    string Name,
    IReadOnlyList<AirportRunway> Runways)
{
    public bool HasRunways => Runways.Count > 0;

    /// <summary>The longest runway, i.e. the runway of the airport that matters.</summary>
    public AirportRunway? LongestRunway => Runways.Count == 0
        ? null
        : Runways.MaxBy(runway => runway.LengthMetres);

    public string DisplayName => Name.Length > 0 ? Name : Icao;
}

/// <summary>
/// The world's airports and their runways, read from the table that ships as an
/// embedded resource (see <c>tools/build-airports.py</c>, which builds it from
/// the public-domain OurAirports data). The table is roughly a megabyte of text
/// for about 17 500 airports, so the map can draw the pavement under a parked
/// aircraft without contacting any service.
/// </summary>
public static class AirportReference
{
    private const string ResourceName = "TheEye.Core.Resources.Maps.airports.txt";

    /// <summary>Width assumed for a runway the source left without one, in metres.</summary>
    private const double DefaultRunwayWidthMetres = 45;

    private static readonly Lazy<Catalog> Bundled = new(Load);

    /// <summary>Number of airports in the table, used by the tests.</summary>
    public static int Count => Bundled.Value.Airports.Length;

    /// <summary>
    /// Looks an airport up by its ICAO code ("EGLL", "LPPT"). Case and surrounding
    /// spaces are ignored; a code the table does not know answers null.
    /// </summary>
    public static AirportRecord? Find(string? icao)
    {
        if (string.IsNullOrWhiteSpace(icao))
        {
            return null;
        }

        return Bundled.Value.ByIcao.TryGetValue(icao.Trim().ToUpperInvariant(), out var airport)
            ? airport
            : null;
    }

    /// <summary>
    /// Every airport inside <paramref name="bounds"/>, optionally only the ones
    /// with a runway to draw. The airports are ordered by latitude, so the scan
    /// only walks the band the viewport covers — panning at a high zoom looks at a
    /// handful of records rather than the whole table.
    /// </summary>
    public static IReadOnlyList<AirportRecord> FindInBounds(GeoBounds bounds, bool runwaysOnly = false)
    {
        if (!bounds.IsValid)
        {
            return [];
        }

        var airports = Bundled.Value.Airports;
        var found = new List<AirportRecord>();

        for (var index = LowerBound(airports, bounds.MinLatitude); index < airports.Length; index++)
        {
            var airport = airports[index];
            if (airport.Position.Latitude > bounds.MaxLatitude)
            {
                break;
            }

            if (airport.Position.Longitude < bounds.MinLongitude ||
                airport.Position.Longitude > bounds.MaxLongitude)
            {
                continue;
            }

            if (runwaysOnly && !airport.HasRunways)
            {
                continue;
            }

            found.Add(airport);
        }

        return found;
    }

    /// <summary>
    /// Reads one line of the table (<c>ICAO|lat|lon|name|runways</c>). Exposed
    /// because the format is the contract between the builder and the app, so a
    /// malformed line has to be testable without shipping a broken resource.
    /// </summary>
    public static AirportRecord? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var fields = line.Split('|');
        if (fields.Length < 4)
        {
            return null;
        }

        var icao = fields[0].Trim().ToUpperInvariant();
        if (icao.Length == 0 ||
            !TryNumber(fields[1], out var latitude) ||
            !TryNumber(fields[2], out var longitude))
        {
            return null;
        }

        var position = new GeoCoordinate(latitude, longitude);
        if (!position.IsValid)
        {
            return null;
        }

        var name = fields[3].Trim();
        var runways = new List<AirportRunway>();
        if (fields.Length > 4)
        {
            foreach (var text in fields[4].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (ParseRunway(text) is { } runway)
                {
                    runways.Add(runway);
                }
            }
        }

        return new AirportRecord(icao, position, name.Length > 0 ? name : icao, runways);
    }

    /// <summary>Reads one runway: <c>leLat,leLon,heLat,heLon,widthMetres</c>.</summary>
    private static AirportRunway? ParseRunway(string text)
    {
        var values = text.Split(',');
        if (values.Length < 4 ||
            !TryNumber(values[0], out var startLatitude) ||
            !TryNumber(values[1], out var startLongitude) ||
            !TryNumber(values[2], out var endLatitude) ||
            !TryNumber(values[3], out var endLongitude))
        {
            return null;
        }

        var start = new GeoCoordinate(startLatitude, startLongitude);
        var end = new GeoCoordinate(endLatitude, endLongitude);
        if (!start.IsValid || !end.IsValid || start == end)
        {
            return null;
        }

        var width = values.Length > 4 && TryNumber(values[4], out var widthMetres) && widthMetres > 0
            ? Math.Clamp(widthMetres, 10, 120)
            : DefaultRunwayWidthMetres;

        return new AirportRunway(start, end, width);
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Index of the first airport at or above <paramref name="latitude"/>.</summary>
    private static int LowerBound(AirportRecord[] airports, double latitude)
    {
        var low = 0;
        var high = airports.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (airports[middle].Position.Latitude < latitude)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static Catalog Load()
    {
        using var stream = typeof(AirportReference).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The bundled airport table is missing.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var airports = new List<AirportRecord>(20_000);
        var byIcao = new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            if (Parse(line) is { } airport && byIcao.TryAdd(airport.Icao, airport))
            {
                airports.Add(airport);
            }
        }

        // Latitude order is what FindInBounds relies on, and the file is written
        // sorted by ICAO code, so the records are ordered once here instead.
        airports.Sort((left, right) => left.Position.Latitude.CompareTo(right.Position.Latitude));
        return new Catalog(airports.ToArray(), byIcao);
    }

    private sealed record Catalog(AirportRecord[] Airports, Dictionary<string, AirportRecord> ByIcao);
}
