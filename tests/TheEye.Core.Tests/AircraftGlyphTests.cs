using SkiaSharp;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class AircraftGlyphTests
{
    [Theory]
    [InlineData("A332", AircraftGlyph.HeavyTwin)]
    [InlineData("A35K", AircraftGlyph.HeavyTwin)]
    [InlineData("B763", AircraftGlyph.HeavyTwin)]
    [InlineData("B77W", AircraftGlyph.HeavyTwin)]
    [InlineData("MD11", AircraftGlyph.HeavyTrijet)]
    [InlineData("L101", AircraftGlyph.HeavyTrijet)]
    [InlineData("DC10", AircraftGlyph.HeavyTrijet)]
    [InlineData("D10T", AircraftGlyph.HeavyTrijet)]
    [InlineData("D10F", AircraftGlyph.HeavyTrijet)]
    [InlineData("B752", AircraftGlyph.LongMediumTwin)]
    [InlineData("B753", AircraftGlyph.LongMediumTwin)]
    [InlineData("B748", AircraftGlyph.HeavyQuad)]
    [InlineData("A388", AircraftGlyph.HeavyQuad)]
    [InlineData("A346", AircraftGlyph.HeavyQuad)]
    [InlineData("B703", AircraftGlyph.ClassicQuad)]
    [InlineData("DC87", AircraftGlyph.ClassicQuad)]
    [InlineData("A20N", AircraftGlyph.MediumTwin)]
    [InlineData("B738", AircraftGlyph.MediumTwin)]
    [InlineData("E190", AircraftGlyph.MediumTwin)]
    [InlineData("F100", AircraftGlyph.RegionalRearTwin)]
    [InlineData("CRJX", AircraftGlyph.RegionalRearTwin)]
    [InlineData("E145", AircraftGlyph.RegionalRearTwin)]
    [InlineData("GLF6", AircraftGlyph.BusinessJet)]
    [InlineData("E50P", AircraftGlyph.BusinessJet)]
    [InlineData("C25C", AircraftGlyph.BusinessJet)]
    [InlineData("PC12", AircraftGlyph.TurbopropSingle)]
    [InlineData("C208", AircraftGlyph.TurbopropSingle)]
    [InlineData("AT76", AircraftGlyph.TurbopropTwin)]
    [InlineData("BE20", AircraftGlyph.TurbopropTwin)]
    [InlineData("C172", AircraftGlyph.PistonSingle)]
    [InlineData("SR22", AircraftGlyph.PistonSingle)]
    [InlineData("DA42", AircraftGlyph.PistonTwin)]
    [InlineData("BE58", AircraftGlyph.PistonTwin)]
    [InlineData("EC45", AircraftGlyph.Helicopter)]
    [InlineData("AS50", AircraftGlyph.Helicopter)]
    [InlineData("R44", AircraftGlyph.Helicopter)]
    [InlineData(" b738 ", AircraftGlyph.MediumTwin)]
    [InlineData("ZZZZ", AircraftGlyph.MediumTwin)]
    [InlineData("B738X", AircraftGlyph.MediumTwin)]
    [InlineData("B722", AircraftGlyph.MediumTwin)]
    [InlineData("DHC7", AircraftGlyph.MediumTwin)]
    [InlineData("", AircraftGlyph.MediumTwin)]
    [InlineData(null, AircraftGlyph.MediumTwin)]
    public void ClassifiesNestedAndSummaryCodes(string? code, AircraftGlyph expected)
    {
        Assert.Equal(expected, AircraftIconCatalog.Classify(new IvaoFlightPlan { AircraftId = code }));
        Assert.Equal(expected, AircraftIconCatalog.Classify(new IvaoFlightPlan
        {
            Aircraft = new IvaoAircraftSummary { IcaoCode = code }
        }));
    }

    [Fact]
    public void MetadataPriorityAndMediumTwinFallback()
    {
        Assert.Equal(AircraftGlyph.MediumTwin, AircraftIconCatalog.Classify(null));
        var plan = new IvaoFlightPlan
        {
            AircraftId = "B738",
            Aircraft = new IvaoAircraftSummary { IcaoCode = " ", WakeTurbulence = "H" }
        };
        Assert.Equal(AircraftGlyph.MediumTwin, AircraftIconCatalog.Classify(plan));
        plan.Aircraft.IcaoCode = "A388";
        Assert.Equal(AircraftGlyph.HeavyQuad, AircraftIconCatalog.Classify(plan));
        plan.Aircraft.IcaoCode = "UNKN";
        Assert.Equal(AircraftGlyph.MediumTwin, AircraftIconCatalog.Classify(plan));
        plan.Aircraft.Description = " helicopter ";
        Assert.Equal(AircraftGlyph.Helicopter, AircraftIconCatalog.Classify(plan));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(227, 227)]
    [InlineData(360, 0)]
    [InlineData(721, 1)]
    [InlineData(-1, 359)]
    [InlineData(-360, 0)]
    public void NormalizesHeading(int? heading, int expected) =>
        Assert.Equal(expected, AircraftIconCatalog.NormalizeHeading(heading));

    [Fact]
    public void EveryEmbeddedSvgParsesAndRemainsInsideRotationCircle()
    {
        Assert.DoesNotContain(typeof(AircraftIconCatalog).Assembly.GetManifestResourceNames(),
            name => name.StartsWith("TheEye.Core.Resources.Aircraft.") && name.EndsWith(".png"));
        foreach (var glyph in Enum.GetValues<AircraftGlyph>())
        {
            var data = AircraftIconCatalog.PathData(glyph);
            using var path = SKPath.ParseSvgPathData(data);
            Assert.NotNull(path);
            Assert.False(path.IsEmpty);
            for (var heading = 0; heading < 360; heading++)
            {
                using var rotated = new SKPath(path);
                rotated.Transform(SKMatrix.CreateRotationDegrees(heading, 32, 32));
                var bounds = rotated.TightBounds;
                Assert.InRange(bounds.Left, 0, 64);
                Assert.InRange(bounds.Top, 0, 64);
                Assert.InRange(bounds.Right, 0, 64);
                Assert.InRange(bounds.Bottom, 0, 64);
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => AircraftIconCatalog.Open((AircraftGlyph)999));
    }

    [Fact]
    public void LongMediumTwinIsLongerAndThinnerThanStandardMediumTwin()
    {
        using var standard = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(AircraftGlyph.MediumTwin));
        using var slender = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(AircraftGlyph.LongMediumTwin));
        Assert.True(slender.TightBounds.Height >= standard.TightBounds.Height + 8);
        Assert.True(slender.TightBounds.Width < standard.TightBounds.Width);
        Assert.True(standard.Contains(34.5f, 20));
        Assert.False(slender.Contains(34.5f, 20));
        Assert.True(slender.Contains(32, 20));
    }

    [Fact]
    public void HeavyTrijetHasLargerMoreSweptWingsThanHeavyTwin()
    {
        using var twin = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(AircraftGlyph.HeavyTwin));
        using var trijet = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(AircraftGlyph.HeavyTrijet));
        Assert.True(trijet.TightBounds.Width >= twin.TightBounds.Width + 4);
        Assert.True(twin.Contains(12, 37));
        Assert.False(trijet.Contains(12, 37));
        Assert.False(twin.Contains(6, 45));
        Assert.True(trijet.Contains(6, 45));
        Assert.True(trijet.Contains(58, 45));
        var twinWingArea = 0;
        var trijetWingArea = 0;
        for (var x = 0; x < 28; x++)
        {
            for (var y = 24; y < 49; y++)
            {
                if (twin.Contains(x + 0.5f, y + 0.5f)) twinWingArea++;
                if (trijet.Contains(x + 0.5f, y + 0.5f)) trijetWingArea++;
            }
        }
        Assert.True(trijetWingArea >= twinWingArea * 1.3);
    }

    [Fact]
    public void HeavyTrijetUnderwingEnginesProjectAheadOfWingLeadingEdges()
    {
        using var stream = AircraftIconCatalog.Open(AircraftGlyph.HeavyTrijet);
        var svg = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace ns = "http://www.w3.org/2000/svg";
        var paths = svg.Root!.Elements(ns + "path").ToArray();
        using var airframe = SKPath.ParseSvgPathData((string)paths[0].Attribute("d")!);
        using var engines = SKPath.ParseSvgPathData((string)paths[1].Attribute("d")!);
        foreach (var x in new[] { 20f, 44f })
        {
            Assert.False(airframe.Contains(x, 29));
            Assert.True(engines.Contains(x, 29));
            Assert.False(airframe.Contains(x, 31));
            Assert.True(engines.Contains(x, 31));
            Assert.True(airframe.Contains(x, 36));
            Assert.True(engines.Contains(x, 36));
        }
    }

    [Fact]
    public void HeavyTrijetHasDistinctTailEngineBulge()
    {
        using var twin = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(AircraftGlyph.HeavyTwin));
        using var trijet = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(AircraftGlyph.HeavyTrijet));
        Assert.False(twin.Contains(36, 47));
        Assert.True(trijet.Contains(36, 47));
        Assert.True(trijet.Contains(28, 47));
    }

    [Fact]
    public void HeadingChangesReuseGeometryAndTypeChangesRefreshIt()
    {
        var created = 0;
        var icons = new AircraftIconCache<object>(_ => { created++; return new object(); });
        using var images = new EmptyImages();
        var pilot = new IvaoPilot
        {
            FlightPlan = new IvaoFlightPlan { AircraftId = "B738" },
            LastTrack = new IvaoLastTrack { Heading = 90, OnGround = true }
        };
        var item = new PilotItem(pilot, icons, images) { IsSelected = true };
        var first = item.Icon;
        pilot.LastTrack.Heading = 227;
        item.Update(pilot);
        Assert.Same(first, item.Icon);
        Assert.Equal(227, item.HeadingDegrees);
        Assert.True(item.IsSelected);
        Assert.True(item.IsOnGround);
        Assert.Equal(1, created);
        pilot.FlightPlan.AircraftId = "AT76";
        item.Update(pilot);
        Assert.NotSame(first, item.Icon);
        Assert.Equal(AircraftGlyph.TurbopropTwin, item.Glyph);
        Assert.Equal(2, created);
        item.ApplyFlightPlan(new SessionFlightPlanResult(new IvaoFlightPlan { Route = "DCT" }, null, null));
        Assert.Equal(AircraftGlyph.TurbopropTwin, item.Glyph);
        Assert.Equal(2, created);
        item.ApplyFlightPlan(new SessionFlightPlanResult(new IvaoFlightPlan { AircraftId = "EC45" }, null, null));
        Assert.Equal(AircraftGlyph.Helicopter, item.Glyph);
        Assert.Equal(3, created);
        item.ApplyFlightPlan(new SessionFlightPlanResult(new IvaoFlightPlan { AircraftId = "ZZZZ" }, null, null));
        Assert.Equal(AircraftGlyph.MediumTwin, item.Glyph);
        Assert.Same(first, item.Icon);
        Assert.Equal(3, created);
    }

    [Fact]
    public void UnauthorizedProfileAndFlightPlanUseQuietPlaceholders()
    {
        var icons = new AircraftIconCache<object>(_ => new object());
        using var images = new EmptyImages();
        var pilot = new PilotItem(new IvaoPilot(), icons, images);
        var atc = new AtcItem(new IvaoAtc(), images);

        pilot.ApplySessionDetail(new SessionDetailResult(null, 401));
        pilot.ApplyFlightPlan(new SessionFlightPlanResult(null, 401));
        atc.ApplySessionDetail(new SessionDetailResult(null, 401));

        Assert.True(pilot.HasProfile);
        Assert.Equal("-", pilot.ProfileName);
        Assert.Equal("-", pilot.PilotId);
        Assert.Equal("-", pilot.HomeText);
        Assert.Equal("-", pilot.RatingText);
        Assert.Equal(string.Empty, pilot.ProfileNotice);
        Assert.Equal("-", pilot.FlightRules);
        Assert.Equal("-", pilot.CruisingLevel);
        Assert.Equal("-", pilot.EstimatedEnroute);
        Assert.Equal(string.Empty, pilot.FlightPlanNotice);

        Assert.True(atc.HasProfile);
        Assert.Equal("-", atc.ProfileName);
        Assert.Equal("-", atc.ControllerId);
        Assert.Equal("-", atc.HomeText);
        Assert.Equal("-", atc.RatingText);
        Assert.Equal(string.Empty, atc.ProfileNotice);
    }

    [Fact]
    public void CacheCreatesEachCategoryOnlyOnceUnderConcurrentAccess()
    {
        var created = 0;
        var cache = new AircraftIconCache<object>(_ =>
        {
            Interlocked.Increment(ref created);
            return new object();
        });
        var icons = new object[100];
        Parallel.For(0, icons.Length, index => icons[index] = cache.Get(AircraftGlyph.HeavyTwin));
        Assert.Equal(1, created);
        Assert.All(icons, icon => Assert.Same(icons[0], icon));
    }

    private sealed class EmptyImages : IImageCache
    {
        public object? TryGet(string? url) => null;
        public Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);
        public void Dispose() { }
    }
}
