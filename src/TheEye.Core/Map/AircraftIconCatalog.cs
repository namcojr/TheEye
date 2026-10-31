namespace TheEye.Core.Maps;

public static class AircraftIconCatalog
{
    public const int HeadingCount = 361;
    private const string ResourcePrefix = "TheEye.Core.Resources.Aircraft.";

    private static readonly Lazy<HashSet<string>> ResourceNames = new(LoadResourceNames);

    /// <summary>
    /// The bundled SAS Acars/IVAO icon set contains one PNG per degree of
    /// heading (0 through 360). Headings are wrapped into that range.
    /// </summary>
    public static int NormalizeHeading(int? heading)
    {
        var value = heading ?? 0;
        value %= 360;
        if (value < 0)
        {
            value += 360;
        }

        return value;
    }

    public static bool Contains(int heading)
    {
        var normalized = NormalizeHeading(heading);
        return ResourceNames.Value.Contains($"{ResourcePrefix}{normalized}.png");
    }

    public static Stream Open(int heading)
    {
        var normalized = NormalizeHeading(heading);
        return typeof(AircraftIconCatalog).Assembly.GetManifestResourceStream($"{ResourcePrefix}{normalized}.png")
            ?? throw new InvalidOperationException($"Aircraft icon {normalized}° is missing.");
    }

    public static Stream OpenFallback()
    {
        var fallback = Contains(0) ? 0 : NormalizeHeading(0);
        return Open(fallback);
    }

    private static HashSet<string> LoadResourceNames()
    {
        return typeof(AircraftIconCatalog).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }
}
