using System.ComponentModel.DataAnnotations;

namespace CommunitySos.Application.Common.Options;

/// <summary>
/// Storage limitation (DPDP s.8(7), GDPR Art. 5(1)(e)), bound from "Retention". Applied daily by
/// RetentionPurgeJob (docs/07-testing-deployment.md#retention).
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>SOS photos are deleted this many days after the SOS closed (resolved, cancelled, rejected, expired).</summary>
    [Range(1, 3650)]
    public int SosMediaDays { get; init; } = 90;

    /// <summary>Audit log rows are kept this long (DPDP Rules: 1 year; CERT-In: 180 days minimum).</summary>
    [Range(180, 3650)]
    public int AuditLogDays { get; init; } = 365;

    /// <summary>Expired or revoked refresh tokens and revoked location keys are kept this long for investigations, then deleted.</summary>
    [Range(1, 365)]
    public int EndedSessionDays { get; init; } = 30;

    /// <summary>Devices deactivated (push unregistered, signed out) and unseen for this long are deleted.</summary>
    [Range(1, 365)]
    public int InactiveDeviceDays { get; init; } = 90;

    /// <summary>A last-known location older than this is useless for alerts (they ignore anything over 12 h) and is deleted.</summary>
    [Range(12, 720)]
    public int StaleLocationHours { get; init; } = 48;

    /// <summary>Rows deleted per round trip, so a large first run never holds long locks.</summary>
    [Range(100, 10_000)]
    public int BatchSize { get; init; } = 1_000;
}
