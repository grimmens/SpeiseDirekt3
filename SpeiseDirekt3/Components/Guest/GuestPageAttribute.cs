namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// Marks a page as a guest page (<c>/qr/{id}</c>, <c>/menu/{id}</c>, <c>/menu/preview/{id}</c>). <c>App.razor</c>
/// then renders the guest document (<see cref="OwnDocumentAttribute"/>): its own head (<c>css/guest-*.css</c>,
/// <c>js/guest-menu.js</c>, self-hosted fonts), <c>&lt;html lang&gt;</c> from <see cref="GuestDocument"/> and
/// <c>data-guest-page</c>. Always combine it with <c>[ExcludeFromInteractiveRouting]</c>.
/// </summary>
public sealed class GuestPageAttribute : OwnDocumentAttribute
{
    public override PageDocument Document => PageDocument.Guest;
}
