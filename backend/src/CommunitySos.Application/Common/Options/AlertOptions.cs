using System.ComponentModel.DataAnnotations;

namespace CommunitySos.Application.Common.Options;

/// <summary>Neighbour alerts for an approved SOS, bound from "Alerts" (passed to dbo.usp_SelectSosRecipients).</summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>People whose last-known location is within this distance of the SOS are alerted.</summary>
    [Range(100, 50_000)]
    public double RadiusMeters { get; init; } = 5_000;

    /// <summary>Last-known locations older than this are ignored (the person has probably moved).</summary>
    [Range(1, 12)]
    public int FreshHours { get; init; } = 12;

    /// <summary>Fixes less accurate than this are ignored (a 3 km error makes "within 5 km" meaningless).</summary>
    [Range(10, 5_000)]
    public double MaxAccuracyMeters { get; init; } = 500;

    /// <summary>Push attempts per recipient before giving up.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; init; } = 5;

    /// <summary>An alert not delivered within this time is dropped by FCM: help is needed now, not tomorrow.</summary>
    [Range(1, 240)]
    public int TimeToLiveMinutes { get; init; } = 30;
}
