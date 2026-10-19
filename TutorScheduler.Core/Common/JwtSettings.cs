namespace TutorScheduler.Core.Common;

// Plain options POCO - Issuer/Audience/ExpiryMinutes come from appsettings.json,
// Secret comes from User Secrets in dev (never committed to the repo).
public class JwtSettings
{
    public string Secret { get; set; } = default!;
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public int ExpiryMinutes { get; set; }
}
