namespace CommunitySos.Domain.Common;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is a stable, machine-readable identifier
/// (e.g. "sos.invalid_transition") that the API returns to clients for localisation.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
