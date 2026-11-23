namespace SpeiseDirekt3.Components;

/// <summary>Which HTML document <c>App.razor</c> renders around a page.</summary>
public enum PageDocument
{
    /// <summary>The back office (and the Identity pages): Tailwind (<c>css/tailwind.css</c>), Bootstrap, the chrome, <c>blazor.web.js</c>.</summary>
    BackOffice,

    /// <summary>The guest pages (<c>/qr</c>, <c>/menu</c>, <c>/menu/preview</c>): <c>css/guest-*.css</c>, <c>js/guest-menu.js</c>.</summary>
    Guest,

    /// <summary>The landing page (<c>/</c>): <c>css/brand.css</c>, <c>css/landing.css</c>, <c>js/landing.js</c>.</summary>
    Landing,

    /// <summary>
    /// The anonymous account pages (<c>/Account/Login</c>, <c>/Account/Register</c>, …; not <c>/Account/Manage</c>):
    /// <c>css/brand.css</c>, <c>css/auth.css</c>, no script at all (plain static SSR forms).
    /// </summary>
    Auth
}

/// <summary>
/// Marks a page that renders <b>its own document</b> in <c>App.razor</c>: static SSR, only its own CSS/JS (plus the
/// self-hosted fonts and the favicons), no Tailwind, Bootstrap, Chart.js, back-office scripts or
/// <c>blazor.web.js</c>, no <c>&lt;base href&gt;</c> (every URL on such a page is absolute). The concrete markers are
/// <see cref="Guest.GuestPageAttribute"/>, <see cref="Landing.LandingPageAttribute"/> and <see cref="Account.AuthPageAttribute"/>.
/// Always combine it with <c>[ExcludeFromInteractiveRouting]</c>, which keeps the page static SSR (no render mode, no
/// circuit). The interactive router of the back office then does not know the page; its <c>NotFound</c>
/// (<c>Shared/ServerPageLoad</c>) loads it from the server (full page load).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public abstract class OwnDocumentAttribute : Attribute
{
    public abstract PageDocument Document { get; }
}
