using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>Display-only dead reckoning; never modifies the API track.</summary>
public sealed class AircraftMotion(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaximumPredictionAge = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CorrectionDuration = TimeSpan.FromSeconds(1);
    private const double EarthRadiusNm = 3440.065;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private Sample? _sample;
    private long _receivedAt;
    private double _ageAtReceipt;
    private double _latitudeCorrection;
    private double _longitudeCorrection;

    public GeoCoordinate? Position
    {
        get
        {
            if (_sample is not { } sample) return null;
            var elapsed = _clock.GetElapsedTime(_receivedAt).TotalSeconds;
            var position = Predict(sample, Math.Clamp(_ageAtReceipt + elapsed, 0, MaximumPredictionAge.TotalSeconds));
            var fraction = Math.Clamp(elapsed / CorrectionDuration.TotalSeconds, 0, 1);
            var remaining = 1 - fraction * fraction * (3 - 2 * fraction);
            return new GeoCoordinate(
                Math.Clamp(position.Latitude + _latitudeCorrection * remaining, -90, 90),
                NormalizeLongitude(position.Longitude + _longitudeCorrection * remaining));
        }
    }

    public void Update(IvaoLastTrack? track)
    {
        if (track?.Position is not { } position)
        {
            _sample = null;
            return;
        }

        var sample = new Sample(position, track.GroundSpeed, track.Heading, track.Timestamp);
        // Cached/duplicate responses must not restart movement or the stale-data limit.
        if (sample == _sample ||
            sample.Timestamp is { } timestamp && _sample?.Timestamp is { } previous && timestamp < previous)
        {
            return;
        }

        var displayed = Position;
        _sample = sample;
        _receivedAt = _clock.GetTimestamp();
        _ageAtReceipt = sample.Timestamp is { } reportedAt
            ? Math.Clamp((_clock.GetUtcNow() - reportedAt).TotalSeconds, 0, MaximumPredictionAge.TotalSeconds)
            : 0;
        var target = Predict(sample, _ageAtReceipt);
        _latitudeCorrection = displayed is { } old ? old.Latitude - target.Latitude : 0;
        _longitudeCorrection = displayed is { } oldPosition
            ? NormalizeLongitude(oldPosition.Longitude - target.Longitude)
            : 0;
    }

    private static GeoCoordinate Predict(Sample sample, double seconds)
    {
        if (sample.Speed is not > 0 || sample.Heading is not { } heading || seconds <= 0)
            return sample.Position;

        var distance = sample.Speed.Value * seconds / 3600 / EarthRadiusNm;
        var bearing = AircraftIconCatalog.NormalizeHeading(heading) * Math.PI / 180;
        var latitude = sample.Position.Latitude * Math.PI / 180;
        var longitude = sample.Position.Longitude * Math.PI / 180;
        var destinationLatitude = Math.Asin(Math.Clamp(
            Math.Sin(latitude) * Math.Cos(distance) +
            Math.Cos(latitude) * Math.Sin(distance) * Math.Cos(bearing), -1, 1));
        var destinationLongitude = longitude + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(distance) * Math.Cos(latitude),
            Math.Cos(distance) - Math.Sin(latitude) * Math.Sin(destinationLatitude));
        return new GeoCoordinate(destinationLatitude * 180 / Math.PI,
            NormalizeLongitude(destinationLongitude * 180 / Math.PI));
    }

    private static double NormalizeLongitude(double longitude) => ((longitude + 180) % 360 + 360) % 360 - 180;

    private sealed record Sample(GeoCoordinate Position, int? Speed, int? Heading, DateTimeOffset? Timestamp);
}
