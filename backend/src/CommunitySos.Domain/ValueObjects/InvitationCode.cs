using System.Security.Cryptography;

namespace CommunitySos.Domain.ValueObjects;

/// <summary>
/// Short, URL-safe, crypto-random invite code. The alphabet excludes look-alikes (0/O, 1/I/L)
/// so codes can be typed by hand when deep links are unavailable.
/// 31^10 ≈ 8×10^14 combinations; the API additionally rate-limits code lookups.
/// </summary>
public static class InvitationCode
{
    public const int Length = 10;
    public const int MaxLength = 16;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsWellFormed(string? code)
    {
        var normalized = Normalize(code);
        return normalized.Length == Length && normalized.All(c => Alphabet.Contains(c, StringComparison.Ordinal));
    }
}
