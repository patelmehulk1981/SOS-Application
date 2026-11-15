using System.ComponentModel.DataAnnotations;

namespace CommunitySos.Application.Common.Options;

/// <summary>Product-level settings, bound from configuration section "App".</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Current Terms/Privacy Notice version. Users who accepted an older one must re-accept.</summary>
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string PolicyVersion { get; init; } = null!;

    [Required]
    public MinimumAppVersionOptions MinimumAppVersion { get; init; } = new();

    /// <summary>Shown on the SOS screen; first entry is the primary number (112 in India).</summary>
    [MinLength(1)]
    public List<EmergencyNumberOptions> EmergencyNumbers { get; init; } = [];

    /// <summary>
    /// Public base URL of invitation links, e.g. https://sos.example.in → https://sos.example.in/i/K7QX2M9PRA.
    /// The domain must host Android App Links / iOS Universal Links files (Module 4).
    /// </summary>
    [Required]
    [Url]
    public string InviteLinkBaseUrl { get; init; } = null!;

    /// <summary>Grievance officer contact (DPDP Act s.13). Shown in Settings → Privacy.</summary>
    public string? GrievanceContactEmail { get; init; }
}

public sealed class MinimumAppVersionOptions
{
    public string Android { get; init; } = "1.0.0";

    public string Ios { get; init; } = "1.0.0";
}

public sealed class EmergencyNumberOptions
{
    public string Label { get; init; } = null!;

    public string Number { get; init; } = null!;
}
