namespace CommunitySos.Domain.Common;

internal static class Text
{
    /// <summary>Trims the value, converts blank to null and enforces a maximum length.</summary>
    public static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{field}.too_long", $"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static string Required(string? value, int maxLength, string field) =>
        Optional(value, maxLength, field)
        ?? throw new DomainException($"{field}.required", $"{field} is required.");

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
