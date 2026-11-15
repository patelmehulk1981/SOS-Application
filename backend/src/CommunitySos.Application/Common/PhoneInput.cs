using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Application.Common;

/// <summary>User-typed phone numbers: accepts Indian 10-digit mobiles (with optional leading 0) as +91.</summary>
public static class PhoneInput
{
    public static PhoneNumber? ParseOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = string.Concat(raw.Where(char.IsAsciiDigit));
        var trimmed = raw.TrimStart();

        if (!trimmed.StartsWith('+'))
        {
            if (digits.Length == 11 && digits[0] == '0')
            {
                digits = digits[1..];
            }

            if (digits.Length == 10 && digits[0] is >= '6' and <= '9')
            {
                return PhoneNumber.Create("+91" + digits);
            }
        }

        return PhoneNumber.Create(raw);
    }
}
