namespace SpeiseDirekt3.Components.Plausible;

/// <summary>
/// The page counter (Plausible CE, AP16), read <b>per request</b> from the section <c>Plausible</c>: <c>ScriptPath</c>
/// (the tracker, <c>/_s/script.js</c>) and <c>Endpoint</c> (where it posts, <c>/_s/event</c>). Both are paths on this site:
/// in production the vhost of <c>app.speisedirekt.de</c> on server2 proxies exactly these two paths to the Plausible
/// instance, so the browser never talks to another host and the app never names it. Only
/// <c>appsettings.Production.json</c> sets them; <c>appsettings.json</c> keeps them empty, so Development, E2E and
/// Preview render nothing (no tag, no init script, no event).
/// <para>
/// On only when <b>both</b> are set. A value that is not a root-relative path (an absolute URL, <c>//host</c>, a
/// backslash, whitespace) switches it off as well: an empty <c>Endpoint</c> would make the tracker post to its built-in
/// host, an absolute one would leave the site.
/// </para>
/// </summary>
public sealed record PlausibleSettings(string? ScriptPath, string? Endpoint)
{
    public const string SectionName = "Plausible";

    /// <summary>No tag and no event.</summary>
    public static readonly PlausibleSettings Off = new(null, null);

    public bool Enabled => ScriptPath is not null && Endpoint is not null;

    public static PlausibleSettings From(IConfiguration configuration)
    {
        var scriptPath = LocalPath(configuration[$"{SectionName}:ScriptPath"]);
        var endpoint = LocalPath(configuration[$"{SectionName}:Endpoint"]);
        return scriptPath is null || endpoint is null ? Off : new PlausibleSettings(scriptPath, endpoint);
    }

    /// <summary>A path on this site (<c>/…</c>, not <c>//…</c>), else null.</summary>
    private static string? LocalPath(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value.StartsWith("//", StringComparison.Ordinal))
            return null;
        return value.Any(c => c == '\\' || char.IsWhiteSpace(c) || char.IsControl(c)) || value.Contains("://", StringComparison.Ordinal)
            ? null
            : value;
    }
}
