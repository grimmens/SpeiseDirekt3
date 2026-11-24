namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>Environment switches for the E2E tests.</summary>
public static class E2ESettings
{
    /// <summary>
    /// Set to <c>1</c>/<c>true</c> to let the browser load resources from other hosts. Default: blocked, so the tests run
    /// offline and deterministically. Since AP15b the app requests nothing from another host (Tailwind is the local
    /// css/tailwind.css), so neither the tests nor the screenshot tours need it; it stays as an opt-in.
    /// </summary>
    public const string AllowExternalRequestsEnvVar = "E2E_ALLOW_EXTERNAL_REQUESTS";

    /// <summary>
    /// Browser language of every test context unless a test asks for another one (<c>Accept-Language</c>,
    /// <c>navigator.language</c>). The app picks German or English from it (AP12); the tests' English assertions
    /// assume English, tests of the German UI pass <c>de-DE</c> explicitly.
    /// </summary>
    public const string DefaultLocale = "en-US";

    /// <summary>German browser (<c>de-DE</c>) for tests of the German UI.</summary>
    public const string GermanLocale = "de-DE";

    /// <summary>
    /// Browser language of the screenshot tours (<c>AdminScreenshotTour</c>, <c>LandingScreenshotTour.Capture_landing_page</c>),
    /// e.g. <c>E2E_LOCALE=de-DE</c> for the German back office. Default <see cref="DefaultLocale"/>.
    /// </summary>
    public const string LocaleEnvVar = "E2E_LOCALE";

    public static string TourLocale =>
        Environment.GetEnvironmentVariable(LocaleEnvVar) is { Length: > 0 } locale ? locale : DefaultLocale;

    public static bool AllowExternalRequests =>
        Environment.GetEnvironmentVariable(AllowExternalRequestsEnvVar) is { } v
        && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
}
