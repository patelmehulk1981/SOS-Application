using System.ComponentModel.DataAnnotations;

namespace CommunitySos.Application.Common.Options;

/// <summary>SOS workflow timings, bound from "Sos".</summary>
public sealed class SosOptions
{
    public const string SectionName = "Sos";

    /// <summary>A Pending SOS not reviewed within this time is escalated to the Master Admin.</summary>
    [Range(1, 60)]
    public int EscalationMinutes { get; init; } = 3;

    /// <summary>Pending/Escalated SOS older than this are expired automatically.</summary>
    [Range(1, 168)]
    public int PendingExpiryHours { get; init; } = 24;

    /// <summary>Approved SOS older than this (since approval) are expired automatically.</summary>
    [Range(1, 168)]
    public int ApprovedExpiryHours { get; init; } = 24;
}
