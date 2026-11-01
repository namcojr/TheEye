using System.Xml.Linq;
using TheEye.Core.Models;

namespace TheEye.Core.Maps;

public enum AircraftGlyph
{
    HeavyTwin,
    HeavyQuad,
    ClassicQuad,
    MediumTwin,
    RegionalRearTwin,
    BusinessJet,
    TurbopropSingle,
    TurbopropTwin,
    PistonSingle,
    PistonTwin,
    Helicopter,
    HeavyTrijet,
    LongMediumTwin
}

public static class AircraftIconCatalog
{
    public const int ViewBoxSize = 64;
    private const string ResourcePrefix = "TheEye.Core.Resources.Aircraft.";
    private static readonly IReadOnlyDictionary<string, AircraftGlyph> Types = BuildTypes();
    private static readonly Lazy<IReadOnlyDictionary<AircraftGlyph, string>> Paths = new(LoadPaths);

    public static int NormalizeHeading(int? heading)
    {
        var value = (heading ?? 0) % 360;
        return value < 0 ? value + 360 : value;
    }

    public static AircraftGlyph Classify(IvaoFlightPlan? plan)
    {
        var aircraft = plan?.Aircraft;
        if (string.Equals(aircraft?.Description?.Trim(), "Helicopter", StringComparison.OrdinalIgnoreCase))
        {
            return AircraftGlyph.Helicopter;
        }

        var code = string.IsNullOrWhiteSpace(aircraft?.IcaoCode) ? plan?.AircraftId : aircraft.IcaoCode;
        return code is not null && Types.TryGetValue(code.Trim(), out var glyph)
            ? glyph
            : AircraftGlyph.MediumTwin;
    }

    /// <summary>Combined paths; shells must preserve SVG's nonzero fill rule.</summary>
    public static string PathData(AircraftGlyph glyph) => Paths.Value[glyph];

    public static Stream Open(AircraftGlyph glyph)
    {
        var name = glyph switch
        {
            AircraftGlyph.HeavyTwin => "heavy-twin",
            AircraftGlyph.HeavyTrijet => "heavy-trijet",
            AircraftGlyph.LongMediumTwin => "long-medium-twin",
            AircraftGlyph.HeavyQuad => "heavy-quad",
            AircraftGlyph.ClassicQuad => "classic-quad",
            AircraftGlyph.MediumTwin => "medium-twin",
            AircraftGlyph.RegionalRearTwin => "regional-rear-twin",
            AircraftGlyph.BusinessJet => "business-jet",
            AircraftGlyph.TurbopropSingle => "turboprop-single",
            AircraftGlyph.TurbopropTwin => "turboprop-twin",
            AircraftGlyph.PistonSingle => "piston-single",
            AircraftGlyph.PistonTwin => "piston-twin",
            AircraftGlyph.Helicopter => "helicopter",
            _ => throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "Unknown glyph category.")
        };
        return typeof(AircraftIconCatalog).Assembly.GetManifestResourceStream($"{ResourcePrefix}{name}.svg")
            ?? throw new InvalidOperationException($"Aircraft glyph {name}.svg is missing.");
    }

    private static IReadOnlyDictionary<AircraftGlyph, string> LoadPaths()
    {
        var paths = new Dictionary<AircraftGlyph, string>();
        XNamespace svg = "http://www.w3.org/2000/svg";
        foreach (var glyph in Enum.GetValues<AircraftGlyph>())
        {
            using var stream = Open(glyph);
            var root = XDocument.Load(stream).Root
                ?? throw new InvalidOperationException($"Aircraft glyph {glyph} has no SVG root.");
            // These bundled, monochrome SVGs deliberately use only untransformed paths.
            if (root.Name != svg + "svg" || (string?)root.Attribute("viewBox") != "0 0 64 64" ||
                (string?)root.Attribute("fill") != "currentColor" ||
                root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration &&
                    attribute.Name != "viewBox" && attribute.Name != "fill") ||
                root.Elements().Any(element => element.Name != svg + "title" &&
                    (element.Name != svg + "path" || element.Attributes().Any(attribute => attribute.Name != "d"))))
            {
                throw new InvalidOperationException($"Aircraft glyph {glyph} uses unsupported SVG geometry.");
            }

            var data = root.Elements(svg + "path").Select(path => (string?)path.Attribute("d")).ToArray();
            if (data.Length == 0 || data.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException($"Aircraft glyph {glyph} contains an empty path.");
            }
            paths.Add(glyph, string.Join(" ", data));
        }
        return paths;
    }

    private static IReadOnlyDictionary<string, AircraftGlyph> BuildTypes()
    {
        var types = new Dictionary<string, AircraftGlyph>(StringComparer.OrdinalIgnoreCase);
        Add(AircraftGlyph.HeavyTwin,
            "A306 A30B A310 A332 A333 A337 A338 A339 A359 A35K B762 B763 B764 B772 B773 B77L B77W B778 B779 B788 B789 B78X");
        Add(AircraftGlyph.HeavyQuad,
            "A342 A343 A345 A346 A388 B741 B742 B743 B744 B748 B74R B74S");
        Add(AircraftGlyph.HeavyTrijet, "MD11 DC10 D10T D10F L101");
        Add(AircraftGlyph.LongMediumTwin, "B752 B753");
        Add(AircraftGlyph.ClassicQuad, "B701 B703 B707 DC81 DC82 DC83 DC85 DC86 DC87");
        Add(AircraftGlyph.MediumTwin,
            "A318 A319 A320 A321 A19N A20N A21N B731 B732 B733 B734 B735 B736 B737 B738 B739 B37M B38M B39M B3XM " +
            "BCS1 BCS3 E170 E175 E75L E75S E190 E195 E290 E295 C919 SU95 T204 T214");
        Add(AircraftGlyph.RegionalRearTwin,
            "F28 F70 F100 CRJ1 CRJ2 CRJ7 CRJ9 CRJX E135 E145 E45X E45P DC9 DC91 DC92 DC93 DC94 DC95 MD81 MD82 MD83 MD87 MD88 MD90 B712");
        Add(AircraftGlyph.BusinessJet,
            "C500 C501 C510 C525 C25A C25B C25C C25M C550 C551 C560 C56X C650 C680 C68A C700 C750 " +
            "CL30 CL35 CL60 CL64 GL5T GL7T GLEX GLF2 GLF3 GLF4 GLF5 GLF6 GA5C GA6C GA7C GA8C " +
            "LJ23 LJ24 LJ25 LJ28 LJ31 LJ35 LJ40 LJ45 LJ55 LJ60 LJ70 LJ75 " +
            "FA10 FA20 F2TH FA50 FA7X FA8X F900 F9EX H25A H25B H25C H400 PRM1 BE40 ASTR G150 G280 " +
            "E50P E55P E545 E550 E35L HDJT SF50 PC24 EA50 WW24");
        Add(AircraftGlyph.TurbopropSingle,
            "C208 C10T PC6T PC12 TBM7 TBM8 TBM9 TBM1 P46T M600 M700 KODI AT3T AT4T AT5T AT6T AT8T");
        Add(AircraftGlyph.TurbopropTwin,
            "AT43 AT44 AT45 AT46 AT72 AT73 AT75 AT76 DH8A DH8B DH8C DH8D DHC6 BN2T " +
            "BE9L BE9T BE10 BE20 BE30 BE99 B190 C441 C425 PAY1 PAY2 PAY3 PAY4 P180 " +
            "SF34 SB20 F27 F50 F60 JS31 JS32 JS41 E110 E120 D328 L410 AN24 AN26 AN28 AN32");
        Add(AircraftGlyph.PistonSingle,
            "C150 C152 C162 C170 C172 C177 C180 C182 C185 C188 C206 C207 C210 C400 " +
            "PA18 PA22 PA24 PA28 P28A P28B P28R P28T PA32 P32R P32T PA46 " +
            "SR20 SR22 S22T DA20 DA40 D40D D40T DV20 AA5 AA5A AA5B BE23 BE24 BE33 BE35 BE36 " +
            "M20P M20T DR40 D140 D250 J3 RV4 RV6 RV7 RV8 RV9 RV10 RV12");
        Add(AircraftGlyph.PistonTwin,
            "BE50 BE55 BE58 BE60 BE76 C303 C310 C320 C335 C340 C401 C402 C404 C414 C421 " +
            "PA23 PA27 PA30 PA31 PA34 PA39 PA44 PA60 DA42 DA62 P68 BN2P AC50 AC68 AC90");
        Add(AircraftGlyph.Helicopter,
            "R22 R44 R66 B06 B06T B47 B105 B212 B214 B222 B230 B407 B412 B429 B430 B505 " +
            "AS32 AS50 AS55 AS65 EC20 EC30 EC35 EC45 EC55 EC75 H160 H175 H225 " +
            "A109 A119 A139 A149 A169 A189 S61 S64 S76 S92 UH1 UH60 H500 H269 H60");
        return types;

        void Add(AircraftGlyph glyph, string codes)
        {
            foreach (var code in codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct())
            {
                types.Add(code, glyph);
            }
        }
    }
}
