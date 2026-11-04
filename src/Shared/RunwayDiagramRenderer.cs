using SkiaSharp;
using TheEye.Presentation.ViewModels;

namespace TheEye.Rendering;

/// <summary>Theme colours the runway diagram borrows; the pavement itself keeps fixed colours.</summary>
public readonly record struct RunwayDiagramColors(
    SKColor Text,
    SKColor MutedText,
    SKColor Accent,
    SKColor Headwind,
    SKColor Tailwind,
    SKColor Halo);

/// <summary>
/// Draws the airport panel's runway diagram: a north-up, schematic ICAO
/// runway (threshold piano keys, designators, centreline, aiming points, edge
/// lines) rotated to its true heading and scaled so every heading stays inside
/// the frame, with the METAR wind drawn over it as an arrow. Shared by the WPF
/// and Avalonia shells; all sizes are device-independent pixels.
/// </summary>
public static class RunwayDiagramRenderer
{
    private const float FramePadding = 10;
    private const float MinRunwayWidth = 24;
    private const float MaxRunwayWidth = 40;
    private const float MaxRunwayLength = 340;

    private static readonly SKColor Asphalt = new(0x3A, 0x3F, 0x46);
    private static readonly SKColor Shoulder = new(0x55, 0x5B, 0x63);
    private static readonly SKColor Marking = new(0xF4, 0xF6, 0xF8);

    /// <summary>Where the runway ended up on the canvas, for tests and hit checks.</summary>
    public readonly record struct RunwayLayout(SKPoint Centre, float Length, float Width, float ApproachRoom, float RotationDegrees)
    {
        /// <summary>The corners of everything drawn along the runway, approach marks included.</summary>
        public SKPoint[] Corners()
        {
            var half = Length / 2 + ApproachRoom;
            var radians = RotationDegrees * MathF.PI / 180;
            var (sin, cos) = MathF.SinCos(radians);
            var centre = Centre;
            return
            [
                Map(-half, -Width / 2), Map(half, -Width / 2),
                Map(half, Width / 2), Map(-half, Width / 2)
            ];

            SKPoint Map(float x, float y) => new(centre.X + x * cos - y * sin, centre.Y + x * sin + y * cos);
        }
    }

    /// <summary>
    /// Fits the runway, plus the approach chevrons on both sides, inside the
    /// frame for its heading: the rotated box must clear the padding on every
    /// edge, so diagonal and north-south runways shrink instead of spilling out.
    /// </summary>
    public static RunwayLayout Layout(float width, float height, RunwayDiagramState state)
    {
        var rotation = (float)(state.TrueHeadingDegrees - 90);
        var radians = rotation * MathF.PI / 180;
        var sin = MathF.Abs(MathF.Sin(radians));
        var cos = MathF.Abs(MathF.Cos(radians));
        var halfWidthRoom = Math.Max(0, width / 2 - FramePadding);
        var halfHeightRoom = Math.Max(0, height / 2 - FramePadding);

        // Wider strips draw a little wider, so a 60 m runway does not look like a 23 m one.
        var runwayWidth = Math.Clamp((float)state.WidthMetres * 0.7f, MinRunwayWidth, MaxRunwayWidth);
        runwayWidth = Math.Min(runwayWidth, Math.Min(halfWidthRoom, halfHeightRoom));
        var halfAcross = runwayWidth / 2;

        // Half the along-runway extent that fits: a·|cos| + b·|sin| <= room on x,
        // a·|sin| + b·|cos| <= room on y, solved for a (b is the half width).
        var halfAlong = float.MaxValue;
        if (cos > 1e-4f)
        {
            halfAlong = Math.Min(halfAlong, (halfWidthRoom - halfAcross * sin) / cos);
        }

        if (sin > 1e-4f)
        {
            halfAlong = Math.Min(halfAlong, (halfHeightRoom - halfAcross * cos) / sin);
        }

        halfAlong = Math.Max(0, halfAlong);
        var approachRoom = Math.Min(runwayWidth * 0.75f, halfAlong * 0.18f);
        var length = Math.Min(MaxRunwayLength, Math.Max(0, (halfAlong - approachRoom) * 2));
        return new RunwayLayout(new SKPoint(width / 2, height / 2), length, runwayWidth, approachRoom, rotation);
    }

    /// <summary>ICAO threshold stripes by runway width, capped so they stay legible at diagram scale.</summary>
    public static int ThresholdStripeCount(double widthMetres) => widthMetres switch
    {
        < 23 => 4,
        < 30 => 6,
        _ => 8
    };

    public static void Draw(SKCanvas canvas, float width, float height, RunwayDiagramState? state, RunwayDiagramColors colors)
    {
        if (state is null || width < 40 || height < 40)
        {
            return;
        }

        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        var layout = Layout(width, height, state);

        DrawNorthMark(canvas, colors, typeface);
        if (layout.Length >= 40)
        {
            DrawRunway(canvas, layout, state, colors, typeface);
        }

        DrawWind(canvas, width, height, layout, state, colors, typeface);
    }

    private static void DrawNorthMark(SKCanvas canvas, RunwayDiagramColors colors, SKTypeface typeface)
    {
        using var paint = new SKPaint { Color = colors.MutedText, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var arrow = new SKPath();
        arrow.MoveTo(14, 7);
        arrow.LineTo(19, 18);
        arrow.LineTo(14, 15.5f);
        arrow.LineTo(9, 18);
        arrow.Close();
        canvas.DrawPath(arrow, paint);
        using var font = new SKFont(typeface, 10);
        canvas.DrawText("N", 14, 29, SKTextAlign.Center, font, paint);
    }

    private static void DrawRunway(SKCanvas canvas, RunwayLayout layout, RunwayDiagramState state, RunwayDiagramColors colors, SKTypeface typeface)
    {
        var length = layout.Length;
        var runwayWidth = layout.Width;
        var halfLength = length / 2;
        var halfWidth = runwayWidth / 2;

        canvas.Save();
        canvas.Translate(layout.Centre);
        // Local frame: +x runs from the selected threshold towards the far end,
        // which is the landing direction of the selected runway end.
        canvas.RotateDegrees(layout.RotationDegrees);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        fill.Color = Shoulder;
        canvas.DrawRect(-halfLength - 1.5f, -halfWidth - 1.5f, length + 3, runwayWidth + 3, fill);
        fill.Color = Asphalt;
        canvas.DrawRect(-halfLength, -halfWidth, length, runwayWidth, fill);

        // Side stripes.
        var edgeInset = Math.Max(1.5f, runwayWidth * 0.06f);
        var edgeWidth = Math.Max(1, runwayWidth * 0.035f);
        fill.Color = Marking;
        canvas.DrawRect(-halfLength + 1, -halfWidth + edgeInset, length - 2, edgeWidth, fill);
        canvas.DrawRect(-halfLength + 1, halfWidth - edgeInset - edgeWidth, length - 2, edgeWidth, fill);

        // Per-end markings, measured inward from each threshold.
        var keyLength = runwayWidth * 0.7f;
        var numberGap = runwayWidth * 0.2f;
        var numberSize = runwayWidth * 0.62f;
        var endMargin = Math.Max(2, runwayWidth * 0.08f);
        var markingsDepth = endMargin + keyLength + numberGap + numberSize;
        var hasNumbers = length >= markingsDepth * 2 + runwayWidth * 0.6f;
        var hasAimingPoints = length >= markingsDepth * 2 + runwayWidth * 3.2f;
        var stripes = ThresholdStripeCount(state.WidthMetres);

        DrawEnd(canvas, -halfLength, 1, state.Designation, isSelected: true);
        DrawEnd(canvas, halfLength, -1, state.ReciprocalDesignation, isSelected: false);

        // Centreline, between the two designators.
        var centrelineStart = -halfLength + (hasNumbers ? markingsDepth + numberGap : endMargin + keyLength + numberGap);
        var centrelineEnd = -centrelineStart;
        var dash = Math.Max(6, runwayWidth * 0.45f);
        var gap = dash * 0.75f;
        var centrelineWidth = Math.Max(1.2f, runwayWidth * 0.05f);
        var span = centrelineEnd - centrelineStart;
        if (span > dash)
        {
            var count = (int)((span + gap) / (dash + gap));
            var used = count * dash + (count - 1) * gap;
            var x = centrelineStart + (span - used) / 2;
            for (var i = 0; i < count; i++, x += dash + gap)
            {
                canvas.DrawRect(x, -centrelineWidth / 2, dash, centrelineWidth, fill);
            }
        }

        canvas.Restore();

        // direction is +1 when the runway is used from this end towards +x.
        void DrawEnd(SKCanvas target, float thresholdX, int direction, string designation, bool isSelected)
        {
            // Threshold bar across the runway.
            var barWidth = Math.Max(1.2f, runwayWidth * 0.05f);
            var barX = direction > 0 ? thresholdX + 0.5f : thresholdX - 0.5f - barWidth;
            fill.Color = Marking;
            target.DrawRect(barX, -halfWidth + edgeInset, barWidth, runwayWidth - edgeInset * 2, fill);

            // Piano keys: two banks either side of the centreline. Keys and gaps
            // share one width, and the gap over the centreline is twice as wide.
            var innerWidth = runwayWidth - edgeInset * 2 - edgeWidth * 2 - 2;
            var keyWidth = innerWidth / (stripes * 2);
            var keyStart = thresholdX + direction * endMargin;
            var keyX = direction > 0 ? keyStart : keyStart - keyLength;
            for (var i = 0; i < stripes / 2; i++)
            {
                target.DrawRect(keyX, -innerWidth / 2 + i * keyWidth * 2, keyLength, keyWidth, fill);
                target.DrawRect(keyX, innerWidth / 2 - keyWidth - i * keyWidth * 2, keyLength, keyWidth, fill);
            }

            if (hasNumbers)
            {
                // The designator reads upright to the pilot on final for this end.
                var numberX = thresholdX + direction * (endMargin + keyLength + numberGap + numberSize / 2);
                using var font = new SKFont(typeface, numberSize) { Subpixel = true };
                font.ScaleX = 0.85f;
                target.Save();
                target.Translate(numberX, 0);
                target.RotateDegrees(direction > 0 ? 90 : -90);
                var metrics = font.Metrics;
                var baseline = -(metrics.Ascent + metrics.Descent) / 2;
                target.DrawText(designation, 0, baseline, SKTextAlign.Center, font, fill);
                target.Restore();
            }

            if (hasAimingPoints)
            {
                var aimFrom = thresholdX + direction * Math.Max(markingsDepth + runwayWidth * 1.4f, length * 0.17f);
                var aimLength = runwayWidth * 1.1f;
                var aimWidth = runwayWidth * 0.16f;
                var aimX = direction > 0 ? aimFrom : aimFrom - aimLength;
                var aimOffset = runwayWidth * 0.2f;
                target.DrawRect(aimX, -aimOffset - aimWidth, aimLength, aimWidth, fill);
                target.DrawRect(aimX, aimOffset, aimLength, aimWidth, fill);
            }

            if (isSelected && layout.ApproachRoom >= 6)
            {
                // Chevrons on the approach side show the landing direction.
                stroke.Color = colors.Accent;
                stroke.StrokeWidth = Math.Max(1.5f, runwayWidth * 0.07f);
                stroke.StrokeCap = SKStrokeCap.Round;
                stroke.StrokeJoin = SKStrokeJoin.Round;
                var room = layout.ApproachRoom - 2;
                var chevronDepth = Math.Min(room / 3, runwayWidth * 0.18f);
                var chevronHalf = runwayWidth * 0.28f;
                for (var i = 0; i < 3; i++)
                {
                    var tip = thresholdX - 2 - i * room / 3;
                    using var chevron = new SKPath();
                    chevron.MoveTo(tip - chevronDepth, -chevronHalf);
                    chevron.LineTo(tip, 0);
                    chevron.LineTo(tip - chevronDepth, chevronHalf);
                    target.DrawPath(chevron, stroke);
                }
            }
        }
    }

    private static void DrawWind(SKCanvas canvas, float width, float height, RunwayLayout layout, RunwayDiagramState state,
        RunwayDiagramColors colors, SKTypeface typeface)
    {
        using var font = new SKFont(typeface, 10.5f);
        var speed = $"{state.WindSpeedKnots:0} KT";

        switch (state.WindKind)
        {
            case RunwayWindKind.None:
                DrawBadge(canvas, width, "NO WIND DATA", colors.MutedText, filled: false, font, colors);
                return;
            case RunwayWindKind.Calm:
                DrawBadge(canvas, width, "CALM", colors.Headwind, filled: false, font, colors);
                return;
            case RunwayWindKind.Variable:
                DrawVariableRing(canvas, layout, colors);
                DrawBadge(canvas, width, $"VRB {speed}", colors.MutedText, filled: false, font, colors);
                return;
        }

        if (state.WindDirectionDegrees is not { } direction)
        {
            return;
        }

        var color = state.IsTailwind ? colors.Tailwind : colors.Headwind;

        // The arrow points where the air goes, i.e. away from the reported direction,
        // and never covers the runway markings: a wind across the strip comes in
        // from the upwind side and stops at the edge, pointing at the runway; a
        // wind (nearly) along the strip lies beside it, on its upwind side.
        var toward = (float)((direction + 180) * Math.PI / 180);
        var dx = MathF.Sin(toward);
        var dy = -MathF.Cos(toward);
        var rotation = layout.RotationDegrees * MathF.PI / 180;
        var normal = new SKPoint(-MathF.Sin(rotation), MathF.Cos(rotation));
        var across = dx * normal.X + dy * normal.Y;
        var side = across > 0 ? -1 : 1;
        var centre = layout.Centre;
        SKPoint tail, tip;
        if (MathF.Abs(across) >= 0.34f)
        {
            var edge = layout.Width / 2 + 6;
            tip = new SKPoint(centre.X + normal.X * side * edge, centre.Y + normal.Y * side * edge);
            var length = Math.Min(95, Reach(tip, -dx, -dy, width, height));
            length = Math.Max(24, length);
            tail = new SKPoint(tip.X - dx * length, tip.Y - dy * length);
        }
        else
        {
            var offset = layout.Width / 2 + 11;
            var middle = new SKPoint(centre.X + normal.X * side * offset, centre.Y + normal.Y * side * offset);
            var half = Math.Min(layout.Length / 2 + layout.ApproachRoom,
                Math.Min(Reach(middle, dx, dy, width, height), Reach(middle, -dx, -dy, width, height)));
            half = Math.Max(20, half);
            tail = new SKPoint(middle.X - dx * half, middle.Y - dy * half);
            tip = new SKPoint(middle.X + dx * half, middle.Y + dy * half);
        }
        var shaftWidth = state.IsTailwind ? 4.5f : 3.5f;
        var headLength = 15f;
        var headHalf = 8f;
        var shaftEnd = new SKPoint(tip.X - dx * (headLength - 1), tip.Y - dy * (headLength - 1));

        using var head = new SKPath();
        head.MoveTo(tip);
        head.LineTo(tip.X - dx * headLength - dy * headHalf, tip.Y - dy * headLength + dx * headHalf);
        head.LineTo(tip.X - dx * headLength + dy * headHalf, tip.Y - dy * headLength - dx * headHalf);
        head.Close();

        using var halo = new SKPaint
        {
            Color = colors.Halo.WithAlpha(0xD0), IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = shaftWidth + 4, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawLine(tail, shaftEnd, halo);
        halo.StrokeWidth = 4;
        canvas.DrawPath(head, halo);

        using var paint = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = shaftWidth, StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(tail, shaftEnd, paint);
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(head, paint);

        // The upwind end carries a small dot, like the tail of a wind barb.
        canvas.DrawCircle(tail, shaftWidth * 0.9f, paint);

        var label = $"{direction:000}° {speed}";
        if (state.IsTailwind)
        {
            DrawBadge(canvas, width, $"TAILWIND · {label}", colors.Tailwind, filled: true, font, colors);
        }
        else
        {
            DrawBadge(canvas, width, label, color, filled: false, font, colors);
        }
    }

    /// <summary>How far a ray can travel from <paramref name="from"/> before leaving the padded frame.</summary>
    private static float Reach(SKPoint from, float dx, float dy, float width, float height)
    {
        const float inset = FramePadding + 4;
        var reach = float.MaxValue;
        if (dx > 1e-4f) reach = Math.Min(reach, (width - inset - from.X) / dx);
        if (dx < -1e-4f) reach = Math.Min(reach, (inset - from.X) / dx);
        if (dy > 1e-4f) reach = Math.Min(reach, (height - inset - from.Y) / dy);
        if (dy < -1e-4f) reach = Math.Min(reach, (inset - from.Y) / dy);
        return Math.Max(0, reach);
    }

    private static void DrawVariableRing(SKCanvas canvas, RunwayLayout layout, RunwayDiagramColors colors)
    {
        using var dash = SKPathEffect.CreateDash([5, 4], 0);
        using var paint = new SKPaint
        {
            Color = colors.MutedText, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, PathEffect = dash
        };
        canvas.DrawCircle(layout.Centre, Math.Max(layout.Width, 18), paint);
    }

    /// <summary>Top-right status pill; a filled pill is the attention state.</summary>
    private static void DrawBadge(SKCanvas canvas, float width, string text, SKColor color, bool filled, SKFont font,
        RunwayDiagramColors colors)
    {
        var textWidth = font.MeasureText(text);
        const float padX = 7, padY = 4, top = 7, right = 7;
        var metrics = font.Metrics;
        var textHeight = metrics.Descent - metrics.Ascent;
        var rect = SKRect.Create(width - right - textWidth - padX * 2, top, textWidth + padX * 2, textHeight + padY * 2);
        using var paint = new SKPaint { IsAntialias = true };
        if (filled)
        {
            paint.Color = color;
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawRoundRect(rect, 4, 4, paint);
            paint.Color = colors.Halo;
        }
        else
        {
            paint.Color = colors.Halo.WithAlpha(0xC0);
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawRoundRect(rect, 4, 4, paint);
            paint.Color = color;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            canvas.DrawRoundRect(rect, 4, 4, paint);
            paint.Style = SKPaintStyle.Fill;
        }

        canvas.DrawText(text, rect.MidX, rect.Top + padY - metrics.Ascent, SKTextAlign.Center, font, paint);
    }
}
