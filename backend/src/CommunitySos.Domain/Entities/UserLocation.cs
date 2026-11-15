using CommunitySos.Domain.Common;
using CommunitySos.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// The user's LAST KNOWN location only (one row per user, no history - privacy by design).
/// Used exclusively to find users within the alert radius of an approved SOS.
/// </summary>
public sealed class UserLocation
{
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    private UserLocation()
    {
    }

    public Guid UserId { get; private set; }

    public Point Location { get; private set; } = null!;

    public double? AccuracyMeters { get; private set; }

    /// <summary>When the device took the fix (device clock, validated).</summary>
    public DateTime CapturedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public GeoCoordinate Coordinate => GeoCoordinate.FromPoint(Location);

    public static UserLocation Create(Guid userId, GeoCoordinate coordinate, double? accuracyMeters, DateTime capturedAt, DateTime now)
    {
        Validate(accuracyMeters, capturedAt, now);
        return new UserLocation
        {
            UserId = userId,
            Location = coordinate.ToPoint(),
            AccuracyMeters = accuracyMeters,
            CapturedAt = capturedAt,
            UpdatedAt = now,
        };
    }

    /// <summary>Applies a newer fix. Returns false (and changes nothing) for out-of-order updates.</summary>
    public bool Update(GeoCoordinate coordinate, double? accuracyMeters, DateTime capturedAt, DateTime now)
    {
        Validate(accuracyMeters, capturedAt, now);
        if (capturedAt <= CapturedAt)
        {
            return false;
        }

        Location = coordinate.ToPoint();
        AccuracyMeters = accuracyMeters;
        CapturedAt = capturedAt;
        UpdatedAt = now;
        return true;
    }

    private static void Validate(double? accuracyMeters, DateTime capturedAt, DateTime now)
    {
        if (accuracyMeters is < 0 or > 100_000 || (accuracyMeters is { } a && double.IsNaN(a)))
        {
            throw new DomainException("location.invalid_accuracy", "Location accuracy is not valid.");
        }

        if (capturedAt > now + MaxFutureSkew)
        {
            throw new DomainException("location.future_timestamp", "Location timestamp is in the future.");
        }

        if (capturedAt < now - MaxAge)
        {
            throw new DomainException("location.stale", "Location is too old to be useful.");
        }
    }
}
