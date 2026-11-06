namespace Transcendence.Api.Auth;

public sealed class JwtSettings
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "transcendence-api";

    public string Audience { get; set; } = "transcendence-spa";

    /// <summary>HMAC-SHA256 signing key, at least 32 bytes. Comes from Jwt__Key, never from a committed file.</summary>
    public string Key { get; set; } = "";

    public int LifetimeMinutes { get; set; } = 60;
}
