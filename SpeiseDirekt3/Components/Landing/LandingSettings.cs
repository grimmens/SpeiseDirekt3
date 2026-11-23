namespace SpeiseDirekt3.Components.Landing;

/// <summary>
/// Operator settings of the landing page's and the account pages' footer, read per request from the configuration
/// (empty = not shown): <c>Legal:ImprintUrl</c> and <c>Legal:PrivacyUrl</c> (the imprint is the app's page
/// <c>/impressum</c> since AP15a, the operator's own text, set in <c>appsettings.json</c>; there is no privacy page yet,
/// so <c>PrivacyUrl</c> stays empty) and <c>Landing:ContactEmail</c> (the site's one contact address: footer, imprint,
/// the forgot-password help). URLs must be absolute http(s) or a path on this site.
/// </summary>
public sealed record LandingSettings(string? ImprintUrl, string? PrivacyUrl, string? ContactEmail)
{
    public bool HasLegalLinks => ImprintUrl is not null || PrivacyUrl is not null;

    public static LandingSettings From(IConfiguration configuration) => new(
        Url(configuration["Legal:ImprintUrl"]),
        Url(configuration["Legal:PrivacyUrl"]),
        Email(configuration["Landing:ContactEmail"]));

    private static string? Url(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.StartsWith('/') && !value.StartsWith("//"))
            return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? value
            : null;
    }

    private static string? Email(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && !value.Any(char.IsWhiteSpace) && !value.Contains(':') ? value : null;
    }
}
