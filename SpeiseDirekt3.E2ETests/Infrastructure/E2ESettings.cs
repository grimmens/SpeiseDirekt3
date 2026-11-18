namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>Environment switches for the E2E tests.</summary>
public static class E2ESettings
{
    /// <summary>
    /// Set to <c>1</c>/<c>true</c> to let the browser load external resources (Tailwind CDN, Bootstrap Icons,
    /// fonts), e.g. for layout screenshots. Default: blocked, so the tests run offline and deterministically.
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

    /// <summary>
    /// Minimal stand-in for the Tailwind display utilities the app uses to show/hide elements
    /// (modals use <c>hidden</c>, tables <c>xl:block</c> …). The chrome (top bar, drawer, menu context bar) does not
    /// need it: css/shell.css is local.
    /// Only applied when the CDN is blocked, so visibility in the tests matches the real app.
    /// Icons are capped in size (Tailwind's <c>w-*</c>/<c>h-*</c> are missing, unsized SVGs would fill the page).
    /// </summary>
    public const string TailwindVisibilityCss = """
        svg{width:1.25rem;height:1.25rem}
        .whitespace-nowrap{white-space:nowrap}
        .hidden{display:none}
        @media (min-width:640px){.sm\:flex{display:flex}.sm\:grid{display:grid}}
        @media (min-width:768px){.md\:grid{display:grid}}
        @media (min-width:1024px){.lg\:flex{display:flex}.lg\:block{display:block}.lg\:grid{display:grid}.lg\:hidden{display:none}}
        @media (min-width:1280px){.xl\:block{display:block}.xl\:grid{display:grid}.xl\:hidden{display:none}}
        """;
}
