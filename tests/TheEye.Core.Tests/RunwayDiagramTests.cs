using SkiaSharp;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;
using TheEye.Rendering;

namespace TheEye.Core.Tests;

public sealed class RunwayDiagramTests
{
    private static readonly RunwayDiagramColors Colors = new(
        SKColors.White, SKColors.Gray, SKColors.DeepSkyBlue, SKColors.LimeGreen, SKColors.Red, SKColors.Black);

    public static TheoryData<int, int> Frames => new() { { 326, 230 }, { 248, 230 }, { 538, 230 }, { 200, 120 } };

    [Theory]
    [MemberData(nameof(Frames))]
    public void Runway_StaysInsideTheFrameAtEveryHeading(int width, int height)
    {
        for (var heading = 0; heading < 360; heading += 3)
        {
            var layout = RunwayDiagramRenderer.Layout(width, height, State(heading));

            Assert.True(layout.Length > 0, $"heading {heading}: runway collapsed");
            foreach (var corner in layout.Corners())
            {
                Assert.InRange(corner.X, 0, width);
                Assert.InRange(corner.Y, 0, height);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void Drawing_LeavesTheFrameBorderEmpty(int width, int height)
    {
        for (var heading = 0; heading < 360; heading += 15)
        {
            using var bitmap = Render(width, height, State(heading, windFrom: heading + 135, speed: 18));
            const int band = 4;
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (x >= band && x < width - band && y >= band && y < height - band)
                    {
                        continue;
                    }

                    Assert.True(bitmap.GetPixel(x, y).Alpha == 0, $"heading {heading}: ink at ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public void LongRunways_AreDrawnLongerWhenTheyRunAlongTheWideSide()
    {
        var eastWest = RunwayDiagramRenderer.Layout(326, 230, State(90));
        var northSouth = RunwayDiagramRenderer.Layout(326, 230, State(360));

        Assert.True(eastWest.Length > northSouth.Length);
        Assert.InRange(northSouth.Length, 120, 230);
    }

    [Theory]
    [InlineData(260, 260, 15, RunwayWindKind.Headwind)]
    [InlineData(260, 200, 15, RunwayWindKind.Headwind)]
    [InlineData(260, 80, 8, RunwayWindKind.Tailwind)]
    [InlineData(260, 10, 12, RunwayWindKind.Tailwind)]
    [InlineData(260, 350, 12, RunwayWindKind.Crosswind)]
    [InlineData(260, 260, 0, RunwayWindKind.Calm)]
    public void Wind_IsClassifiedAgainstTheSelectedEnd(double runway, double windFrom, double speed, RunwayWindKind expected) =>
        Assert.Equal(expected, RunwayDiagramState.Classify(runway, new MetarWind(windFrom, speed)));

    [Fact]
    public void Wind_VariableAndMissingAreNotDrawnAsVectors()
    {
        Assert.Equal(RunwayWindKind.Variable, RunwayDiagramState.Classify(90, new MetarWind(null, 4)));
        Assert.Equal(RunwayWindKind.None, RunwayDiagramState.Classify(90, null));
        Assert.False(State(90, windFrom: null, speed: 4).HasWindVector);
    }

    [Fact]
    public void TailwindArrow_UsesTheTailwindColour()
    {
        // Runway 09 with wind from 270: the air blows straight down the strip from behind.
        using var tail = Render(326, 230, State(90, windFrom: 270, speed: 10));
        using var head = Render(326, 230, State(90, windFrom: 90, speed: 10));

        Assert.True(CountColour(tail, SKColors.Red) > 150);
        Assert.Equal(0, CountColour(tail, SKColors.LimeGreen));
        Assert.True(CountColour(head, SKColors.LimeGreen) > 150);
        Assert.Equal(0, CountColour(head, SKColors.Red));
    }

    [Theory]
    [InlineData(18, 4)]
    [InlineData(23, 6)]
    [InlineData(45, 8)]
    [InlineData(60, 8)]
    public void ThresholdStripes_FollowRunwayWidth(double width, int stripes) =>
        Assert.Equal(stripes, RunwayDiagramRenderer.ThresholdStripeCount(width));

    private static RunwayDiagramState State(double heading, double? windFrom = null, double speed = 0)
    {
        var start = new GeoCoordinate(0, 0);
        var radians = heading * Math.PI / 180;
        var end = new GeoCoordinate(0.03 * Math.Cos(radians), 0.03 * Math.Sin(radians));
        var runway = new AirportRunway(start, end, 45);
        var designations = runway.RunwayDesignations.Split('/');
        var option = new RunwayEndOption(designations[0], heading % 360, runway);
        MetarWind? wind = windFrom is null && speed == 0 ? null : new MetarWind(windFrom % 360, speed);
        return RunwayDiagramState.Create(option, wind);
    }

    private static SKBitmap Render(int width, int height, RunwayDiagramState state)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        RunwayDiagramRenderer.Draw(canvas, width, height, state, Colors);
        canvas.Flush();
        return bitmap;
    }

    private static int CountColour(SKBitmap bitmap, SKColor colour) =>
        bitmap.Pixels.Count(pixel => pixel == colour);
}
