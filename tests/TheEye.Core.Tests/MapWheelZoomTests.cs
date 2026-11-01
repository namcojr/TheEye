using Mapsui;
using Mapsui.Extensions;
using Mapsui.Manipulations;
using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class MapWheelZoomTests
{
    private static readonly ScreenPosition Pointer = new(300, 200);

    [Fact]
    public void Zoom_VisitsConsecutiveHalfLevelsAndReturns()
    {
        var navigator = CreateNavigator();
        var wheel = new MapWheelZoom();
        foreach (var level in new[] { 10.5, 11, 11.5, 12, 12.5 })
        {
            wheel.Zoom(navigator, 1, Pointer);
            Assert.Equal(WorldMap.MaxResolution / Math.Pow(2, level), navigator.Viewport.Resolution, 8);
        }

        foreach (var level in new[] { 12, 11.5, 11, 10.5, 10 })
        {
            wheel.Zoom(navigator, -1, Pointer);
            Assert.Equal(WorldMap.MaxResolution / Math.Pow(2, level), navigator.Viewport.Resolution, 8);
        }
    }

    [Fact]
    public void Zoom_LeavesZoomButtonWholeLevelStepsUnchanged()
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;

        navigator.ZoomIn();
        Assert.Equal(initial / 2, navigator.Viewport.Resolution, 8);
        navigator.ZoomOut();
        Assert.Equal(initial, navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public void Zoom_WithPanLockKeepsTheMapCenterFixed()
    {
        var navigator = CreateNavigator();
        navigator.PanLock = true;
        var before = navigator.Viewport;

        new MapWheelZoom().Zoom(navigator, 1, Pointer);

        Assert.Equal(before.CenterX, navigator.Viewport.CenterX, 8);
        Assert.Equal(before.CenterY, navigator.Viewport.CenterY, 8);
        Assert.Equal(before.Resolution / Math.Sqrt(2), navigator.Viewport.Resolution, 8);
    }

    [Theory]
    [InlineData(WorldMap.DeepestZoomLevel - 0.25, 1, WorldMap.MinResolution)]
    [InlineData(WorldMap.MinZoomLevel + 0.25, -1, WorldMap.MaxZoomOutResolution)]
    public void Zoom_ClampsStepsThatCrossZoomBounds(double level, int direction, double expected)
    {
        var navigator = CreateNavigator();
        navigator.ZoomTo(WorldMap.MaxResolution / Math.Pow(2, level));
        var wheel = new MapWheelZoom();

        wheel.Zoom(navigator, direction, Pointer);
        Assert.Equal(expected, navigator.Viewport.Resolution, 8);
        wheel.Zoom(navigator, -direction, Pointer);
        Assert.Equal(expected / Math.Pow(2, -direction * 0.5), navigator.Viewport.Resolution, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(-3)]
    [InlineData(0)]
    public void Zoom_MovesHalfALevelPerFullNotch(int notches)
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;

        new MapWheelZoom().Zoom(navigator, notches, Pointer);

        Assert.Equal(initial / Math.Pow(2, notches * 0.5), navigator.Viewport.Resolution, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Zoom_AccumulatesHighResolutionWindowsAndAvaloniaInput(int direction)
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        for (var i = 0; i < 3; i++)
        {
            wheel.Zoom(navigator, direction * 30 / 120.0, Pointer);
            Assert.Equal(initial, navigator.Viewport.Resolution);
        }

        wheel.Zoom(navigator, direction * 0.25, Pointer);

        Assert.Equal(initial / Math.Pow(2, direction * 0.5), navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public void Zoom_AccumulatesFractionalInputDespiteFloatingPointRounding()
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        for (var i = 0; i < 9; i++)
        {
            wheel.Zoom(navigator, 0.1, Pointer);
            Assert.Equal(initial, navigator.Viewport.Resolution);
        }

        wheel.Zoom(navigator, 0.1, Pointer);

        Assert.Equal(initial / Math.Sqrt(2), navigator.Viewport.Resolution, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Zoom_FromASavedIntermediateResolutionMovesHalfALevel(int direction)
    {
        var navigator = CreateNavigator();
        navigator.ZoomTo(500);

        new MapWheelZoom().Zoom(navigator, direction, Pointer);

        Assert.Equal(500 / Math.Pow(2, direction * 0.5), navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public void Zoom_PreservesRemainderAfterBatchedNotches()
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        wheel.Zoom(navigator, 1.5, Pointer);
        Assert.Equal(initial / Math.Sqrt(2), navigator.Viewport.Resolution, 8);
        wheel.Zoom(navigator, 0, Pointer);
        wheel.Zoom(navigator, 0.5, Pointer);
        Assert.Equal(initial / 2, navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public void Zoom_ReversingDirectionDiscardsTheOldPartialNotch()
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        wheel.Zoom(navigator, 0.75, Pointer);
        wheel.Zoom(navigator, -0.25, Pointer);
        Assert.Equal(initial, navigator.Viewport.Resolution);
        wheel.Zoom(navigator, -0.75, Pointer);
        Assert.Equal(initial * Math.Sqrt(2), navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public void Zoom_KeepsTheLocationUnderThePointerAnchored()
    {
        var navigator = CreateNavigator();
        var before = navigator.Viewport.ScreenToWorld(Pointer);

        new MapWheelZoom().Zoom(navigator, 1, Pointer);

        var after = navigator.Viewport.ScreenToWorld(Pointer);
        Assert.Equal(before.X, after.X, 8);
        Assert.Equal(before.Y, after.Y, 8);
    }

    [Theory]
    [InlineData(WorldMap.DeepestZoomLevel, 1)]
    [InlineData(WorldMap.MinZoomLevel, -1)]
    public void Zoom_RespectsZoomBounds(int level, int direction)
    {
        var navigator = CreateNavigator(level);
        var initial = navigator.Viewport.Resolution;

        new MapWheelZoom().Zoom(navigator, direction, Pointer);

        Assert.Equal(initial, navigator.Viewport.Resolution);
    }

    [Fact]
    public void Zoom_RespectsZoomLock()
    {
        var navigator = CreateNavigator();
        navigator.ZoomLock = true;
        var initial = navigator.Viewport.Resolution;

        new MapWheelZoom().Zoom(navigator, 1, Pointer);

        Assert.Equal(initial, navigator.Viewport.Resolution);
    }

    [Fact]
    public void Zoom_RapidNotchesMoveExactlyHalfALevelEachWithoutAnimationOvershoot()
    {
        var navigator = CreateNavigator();
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        wheel.Zoom(navigator, 1, Pointer);
        wheel.Zoom(navigator, 1, Pointer);
        wheel.Zoom(navigator, 1, Pointer);

        Assert.Equal(initial / Math.Pow(2, 1.5), navigator.Viewport.Resolution, 8);
        Assert.False(navigator.UpdateAnimations());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(-3)]
    public async Task Zoom_AnimatesToExactlyHalfALevelPerNotch(int notches)
    {
        var navigator = CreateNavigator(animate: true);
        var initial = navigator.Viewport.Resolution;

        new MapWheelZoom().Zoom(navigator, notches, Pointer);

        Assert.Equal(initial, navigator.Viewport.Resolution);
        await FinishAnimationAsync(navigator);
        Assert.Equal(initial / Math.Pow(2, notches * 0.5), navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public async Task Zoom_RapidEventsAccumulateFromTheAnimatedTarget()
    {
        var navigator = CreateNavigator(animate: true);
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        wheel.Zoom(navigator, 1, Pointer);
        wheel.Zoom(navigator, 1, Pointer);
        await Task.Delay(40);
        Assert.True(navigator.UpdateAnimations());
        Assert.InRange(navigator.Viewport.Resolution, initial / 2, initial);
        wheel.Zoom(navigator, 1, Pointer);

        await FinishAnimationAsync(navigator);
        Assert.Equal(initial / Math.Pow(2, 1.5), navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public async Task Zoom_ReversingDuringAnimationReturnsToThePreviousLevel()
    {
        var navigator = CreateNavigator(animate: true);
        var initial = navigator.Viewport.Resolution;
        var wheel = new MapWheelZoom();

        wheel.Zoom(navigator, 1, Pointer);
        await Task.Delay(40);
        navigator.UpdateAnimations();
        wheel.Zoom(navigator, -1, Pointer);

        await FinishAnimationAsync(navigator);
        Assert.Equal(initial, navigator.Viewport.Resolution, 8);
    }

    [Fact]
    public async Task Zoom_AnimationKeepsThePointerAnchoredBetweenFrames()
    {
        var navigator = CreateNavigator(animate: true);
        var initial = navigator.Viewport.Resolution;
        var before = navigator.Viewport.ScreenToWorld(Pointer);

        new MapWheelZoom().Zoom(navigator, 1, Pointer);
        await Task.Delay(40);
        navigator.UpdateAnimations();

        Assert.True(navigator.Viewport.Resolution < initial);
        Assert.True(navigator.Viewport.Resolution > initial / Math.Sqrt(2));
        var during = navigator.Viewport.ScreenToWorld(Pointer);
        Assert.Equal(before.X, during.X, 8);
        Assert.Equal(before.Y, during.Y, 8);

        await FinishAnimationAsync(navigator);
        var after = navigator.Viewport.ScreenToWorld(Pointer);
        Assert.Equal(before.X, after.X, 8);
        Assert.Equal(before.Y, after.Y, 8);
    }

    private static async Task FinishAnimationAsync(Navigator navigator)
    {
        await Task.Delay(navigator.MouseWheelAnimation.Duration + 30);
        navigator.UpdateAnimations();
        Assert.False(navigator.UpdateAnimations());
    }

    private static Navigator CreateNavigator(int level = 10, bool animate = false)
    {
        var palette = new MapPalette("#16324F", "#1B2A35", "#2F4858", "#6FE3F5", "#4CC9F0", true);
        var navigator = WorldMap.Create(
            palette, new GeoCoordinate(0, 0), WorldMap.MaxResolution / Math.Pow(2, level)).Navigator;
        navigator.SetSize(800, 600);
        Assert.False(navigator.MouseWheelAnimation.UseContinuousMouseWheelZoom);
        Assert.Equal(200, navigator.MouseWheelAnimation.Duration);
        if (!animate)
        {
            navigator.MouseWheelAnimation.Duration = 0;
        }
        return navigator;
    }
}
