namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// Marks a page as a guest page (<c>/qr/{id}</c>, <c>/menu/{id}</c>, <c>/menu/preview/{id}</c>). <c>App.razor</c>
/// then renders the guest document: its own head (<c>css/guest-*.css</c>, <c>js/guest-menu.js</c>, self-hosted
/// fonts) and no Tailwind CDN, Bootstrap, Chart.js, back-office scripts or <c>blazor.web.js</c>.
/// Always combine it with <c>[ExcludeFromInteractiveRouting]</c>, which keeps the page static SSR
/// (no render mode, no circuit). The interactive router of the back office then does not know the page; its
/// <c>NotFound</c> (<c>Shared/ServerPageLoad</c>) loads it from the server (full page load).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class GuestPageAttribute : Attribute
{
}
