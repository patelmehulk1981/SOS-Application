using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// Append-only consent ledger: every grant AND every withdrawal is a new row, so we can prove exactly
/// what a person agreed to, which notice version they saw, and when (DPDP Act 2023 s.6(10), GDPR Art. 7(1)).
/// Current state = the latest row per (UserId, Type).
/// </summary>
public sealed class UserConsent : Entity<long>
{
    public const int PolicyVersionMaxLength = 20;
    public const int IpMaxLength = 45;
    public const int SourceMaxLength = 50;

    private UserConsent()
    {
    }

    public Guid UserId { get; private set; }

    public ConsentType Type { get; private set; }

    /// <summary>Version of the notice shown to the user, e.g. "2026-11".</summary>
    public string PolicyVersion { get; private set; } = null!;

    public bool Granted { get; private set; }

    public DateTime CapturedAt { get; private set; }

    public string? IpAddress { get; private set; }

    /// <summary>Where consent was captured, e.g. "android/1.0.3" or "ios/1.0.3".</summary>
    public string? Source { get; private set; }

    public static UserConsent Record(
        Guid userId,
        ConsentType type,
        string policyVersion,
        bool granted,
        DateTime now,
        string? ipAddress,
        string? source)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("consent.invalid_type", "Unknown consent type.");
        }

        return new UserConsent
        {
            UserId = userId,
            Type = type,
            PolicyVersion = Text.Required(policyVersion, PolicyVersionMaxLength, nameof(PolicyVersion)),
            Granted = granted,
            CapturedAt = now,
            IpAddress = ipAddress is null ? null : Text.Truncate(ipAddress, IpMaxLength),
            Source = source is null ? null : Text.Truncate(source, SourceMaxLength),
        };
    }
}

/// <summary>What the person agreed to on the sign-up screen.</summary>
/// <param name="PolicyVersion">Terms/Privacy Notice version displayed.</param>
/// <param name="ConfirmedAdult">Declared 18 or older (DPDP treats under-18s as children needing verifiable parental consent).</param>
/// <param name="Source">Capturing client, e.g. "android/1.0.3".</param>
public sealed record RegistrationConsent(string PolicyVersion, bool ConfirmedAdult, string? Source);
