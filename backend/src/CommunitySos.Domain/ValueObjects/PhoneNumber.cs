using System.Text.RegularExpressions;
using CommunitySos.Domain.Common;

namespace CommunitySos.Domain.ValueObjects;

/// <summary>Mobile number in E.164 format, e.g. <c>+919876543210</c>.</summary>
public sealed partial record PhoneNumber
{
    public const int MaxLength = 40; // column width; also fits the anonymised placeholder

    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    /// <summary>Safe for logs and audit: <c>+91*******10</c>.</summary>
    public string Masked => Value.Length > 6
        ? Value[..3] + new string('*', Value.Length - 5) + Value[^2..]
        : "***";

    public static PhoneNumber Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException("phone.required", "Phone number is required.");
        }

        var normalized = string.Concat(raw.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')')));
        if (!E164().IsMatch(normalized))
        {
            throw new DomainException("phone.invalid", "Phone number must be in international format, e.g. +919876543210.");
        }

        return new PhoneNumber(normalized);
    }

    /// <summary>Rehydrates a value already stored in the database. Not for user input.</summary>
    public static PhoneNumber FromPersistence(string value) => new(value);

    /// <summary>Unique, non-dialable placeholder used when an account is deleted.</summary>
    internal static PhoneNumber Anonymised(Guid userId) => new($"deleted:{userId:N}");

    public override string ToString() => Masked;

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();
}
