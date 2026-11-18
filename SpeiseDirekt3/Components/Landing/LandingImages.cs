namespace SpeiseDirekt3.Components.Landing;

/// <summary>
/// A product screenshot of the landing page: <c>wwwroot/images/landing/{Name}-{Width}.webp</c> (1x) and
/// <c>{Name}-{2 × Width}.webp</c> (2x). <see cref="Width"/> × <see cref="Height"/> is the CSS size the page reserves
/// (the <c>width</c>/<c>height</c> attributes). The files come from our own app: the E2E tool
/// <c>LandingScreenshotTour</c> shoots them and writes exactly these names and sizes (see the repo's CLAUDE.md).
/// </summary>
public sealed record LandingImage(string Name, int Width, int Height, string Alt)
{
    /// <summary>Path below <c>wwwroot</c> for the scale (1 or 2).</summary>
    public string PathFor(int scale) => $"{LandingImages.Folder}/{Name}-{Width * scale}.webp";
}

/// <summary>A design theme of the guest menu as the landing page shows it (switcher in the phone mockup).</summary>
public sealed record LandingTheme(string Key, string Label, string Swatch, LandingImage Image);

/// <summary>The one list of the landing page's images (page and screenshot tool read it).</summary>
public static class LandingImages
{
    public const string Folder = "images/landing";

    // Phone screens: the guest menu at 390 × 844 CSS px, shown 300 px wide
    private const int PhoneWidth = 300;
    private const int PhoneHeight = 649;

    private static LandingImage Phone(string name, string alt) => new(name, PhoneWidth, PhoneHeight, alt);

    /// <summary>The six themes in the switcher's order (key = <c>DesignTheme</c> in lower case, swatch = its accent).</summary>
    public static readonly IReadOnlyList<LandingTheme> Themes =
    [
        new("modern", "Modern", "#0f766e", Phone("guest-modern", "Die Speisekarte „Trattoria Lago“ im Design Modern auf einem Handy")),
        new("classic", "Klassisch", "#a14f5c", Phone("guest-classic", "Die Speisekarte „Trattoria Lago“ im Design Klassisch auf einem Handy")),
        new("elegant", "Elegant", "#8f6a0a", Phone("guest-elegant", "Die Speisekarte „Trattoria Lago“ im Design Elegant auf einem Handy")),
        new("minimal", "Minimal", "#1a1a1a", Phone("guest-minimal", "Die Speisekarte „Trattoria Lago“ im Design Minimal auf einem Handy")),
        new("fancy", "Verspielt", "#c8336f", Phone("guest-fancy", "Die Speisekarte „Trattoria Lago“ im Design Verspielt auf einem Handy")),
        new("dark", "Dunkel", "#d27b90", Phone("guest-dark", "Die Speisekarte „Trattoria Lago“ im Design Dunkel auf einem Handy"))
    ];

    /// <summary>The hero's phone: the demo menu with its restaurant profile.</summary>
    public static readonly LandingImage Hero =
        Phone("guest-modern", "Die digitale Speisekarte der „Trattoria Lago“ auf einem Handy: Titelbild, Logo, Kategorien und Gerichte mit Fotos");

    public static readonly LandingImage GuestItem =
        Phone("guest-item", "Ein Gericht im Detail: Foto, Kennzeichnungen, Extras mit Aufpreis und Allergene");

    public static readonly LandingImage GuestFilter =
        Phone("guest-filter", "Der Filter der Speisekarte: Ernährung, Hinweise und Allergene, die ausgeblendet werden");

    public static readonly LandingImage GuestFavorites =
        Phone("guest-favorites", "Die Merkliste eines Gastes mit Mengen, Varianten und Summe");

    public static readonly LandingImage GuestInfo =
        Phone("guest-info", "Das Restaurantprofil in der Speisekarte: Öffnungszeiten, Adresse, Telefon, Website und Links");

    // Back office (desktop, cropped): shown in a browser frame
    public static readonly LandingImage Calendar =
        new("qr-calendar", 640, 440, "Der Kalender eines QR-Codes im Dezember: Fischfreitag jede Woche, Weihnachtsmenü vom 24. bis 26., Silvester am 31.");

    public static readonly LandingImage Editor =
        new("editor-preview", 640, 420, "Der Editor einer Speisekarte mit der Live-Vorschau im Handy-Rahmen");

    public static readonly LandingImage Analytics =
        new("analytics", 640, 340, "Die Statistik: Aufrufe, angetippte Gerichte und Besuche als Zahlen und im Verlauf");

    /// <summary>Every WebP image the page uses (each theme once).</summary>
    public static IEnumerable<LandingImage> All =>
        Themes.Select(t => t.Image)
            .Concat([GuestItem, GuestFilter, GuestFavorites, GuestInfo, Calendar, Editor, Analytics])
            .DistinctBy(i => i.Name);

    /// <summary>Open Graph image (link previews), 1200 × 630 PNG.</summary>
    public const string OpenGraph = Folder + "/og.png";
    public const int OpenGraphWidth = 1200;
    public const int OpenGraphHeight = 630;
}
