namespace CommunitySos.Application.Common.Security;

/// <summary>Claim names carried in our access tokens (short, JWT-registered names where they exist).</summary>
public static class AppClaimTypes
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string TokenId = "jti";

    /// <summary>Privacy-policy version the user had accepted when the token was issued.</summary>
    public const string PolicyVersion = "pv";
}
