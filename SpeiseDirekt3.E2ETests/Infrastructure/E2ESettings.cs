namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>Environment switches for the E2E tests.</summary>
public static class E2ESettings
{
    /// <summary>
    /// Set to <c>1</c>/<c>true</c> to let the browser load external resources (Tailwind CDN, Bootstrap Icons,
    /// fonts), e.g. for layout screenshots. Default: blocked, so the tests run offline and deterministically.
    /// </summary>
    public const string AllowExternalRequestsEnvVar = "E2E_ALLOW_EXTERNAL_REQUESTS";

    public static bool AllowExternalRequests =>
        Environment.GetEnvironmentVariable(AllowExternalRequestsEnvVar) is { } v
        && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Minimal stand-in for the Tailwind display utilities the app uses to show/hide elements
    /// (modals use <c>hidden</c>, the sidebar <c>hidden lg:flex</c>, tables <c>xl:block</c> …).
    /// Only applied when the CDN is blocked, so visibility in the tests matches the real app.
    /// Icons are capped in size (Tailwind's <c>w-*</c>/<c>h-*</c> are missing, unsized SVGs would fill the page).
    /// </summary>
    public const string TailwindVisibilityCss = """
        svg{width:1.25rem;height:1.25rem}
        .whitespace-nowrap{white-space:nowrap}
        .hidden{display:none}
        @media (min-width:640px){.sm\:flex{display:flex}.sm\:grid{display:grid}}
        @media (min-width:768px){.md\:grid{display:grid}}
        @media (min-width:1024px){.lg\:flex{display:flex}.lg\:grid{display:grid}.lg\:hidden{display:none}}
        @media (min-width:1280px){.xl\:block{display:block}.xl\:grid{display:grid}.xl\:hidden{display:none}}
        """;
}
