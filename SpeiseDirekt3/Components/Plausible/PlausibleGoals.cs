namespace SpeiseDirekt3.Components.Plausible;

/// <summary>
/// The conversion events (AP16), German because the operator reads them in the dashboard and makes goals of them. The
/// names are exact; never put personal data (e-mail, restaurant name, ids) into a name or a prop.
/// <list type="bullet">
/// <item><see cref="StartFree"/>: a "register for free" call to action of the landing document (<c>js/landing.js</c>,
/// prop <c>position</c>).</item>
/// <item><see cref="AccountCreated"/>: once after a self-registration (<see cref="AccountCreatedMarkers"/>,
/// <c>js/site.js</c>).</item>
/// <item><see cref="FirstMenu"/>, <see cref="FirstQrCode"/>: the tenant's first menu / QR code was saved
/// (<see cref="PlausibleEvents"/>).</item>
/// <item><see cref="QrDownloaded"/>, <see cref="QrLinkCopied"/>: "Download PNG" and "Copy link" of a QR code
/// (<c>js/qr-codes.js</c>).</item>
/// </list>
/// The scripts write the names they send themselves; this list is the one place in C#.
/// </summary>
public static class PlausibleGoals
{
    public const string StartFree = "Kostenlos starten";
    public const string AccountCreated = "Konto angelegt";
    public const string FirstMenu = "Erste Speisekarte";
    public const string FirstQrCode = "Erster QR-Code";
    public const string QrDownloaded = "QR-Code heruntergeladen";
    public const string QrLinkCopied = "QR-Link kopiert";
}
