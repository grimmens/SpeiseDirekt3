namespace SpeiseDirekt3.Components.Landing;

/// <summary>
/// A product screenshot of the landing page: <c>wwwroot/images/landing/{Name}-{Width}.webp</c> (1x) and
/// <c>{Name}-{2 × Width}.webp</c> (2x). <see cref="Width"/> × <see cref="Height"/> is the CSS size the page reserves
/// (the <c>width</c>/<c>height</c> attributes). A <see cref="PerLanguage"/> image (a back-office screenshot, whose UI is
/// German or English) exists once per language: <c>{Name}-{Width}-de.webp</c> / <c>{Name}-{Width}-en.webp</c>; the page
/// shows the request's. <see cref="AltKey"/> is the alt text's key in <c>Resources/LandingText.resx</c>, formatted with the
/// text of <see cref="AltArgumentKey"/> if set. The files come from our own app: the E2E tool <c>LandingScreenshotTour</c>
/// shoots them and writes exactly these names and sizes (see the repo's CLAUDE.md).
/// </summary>
public sealed record LandingImage(string Name, int Width, int Height, string AltKey, string? AltArgumentKey = null, bool PerLanguage = false)
{
    /// <summary>Path below <c>wwwroot</c> for the scale (1 or 2) and the language (<c>de</c>/<c>en</c>; ignored unless <see cref="PerLanguage"/>).</summary>
    public string PathFor(int scale, string language) =>
        PerLanguage
            ? $"{LandingImages.Folder}/{Name}-{Width * scale}-{language}.webp"
            : $"{LandingImages.Folder}/{Name}-{Width * scale}.webp";
}

/// <summary>A design theme of the guest menu as the landing page shows it (switcher in the phone mockup).</summary>
/// <param name="LabelKey">The theme's name in <c>LandingText</c> (<c>Theme_{key}</c>).</param>
public sealed record LandingTheme(string Key, string LabelKey, string Swatch, LandingImage Image);

/// <summary>The one list of the landing page's images (page and screenshot tool read it).</summary>
public static class LandingImages
{
    public const string Folder = "images/landing";

    /// <summary>The languages of the per-language images and of the Open Graph image.</summary>
    public static readonly IReadOnlyList<string> Languages = ["de", "en"];

    // Phone screens: the guest menu at 390 × 844 CSS px, shown 300 px wide. The guest menu is the demo restaurant's German
    // content, the same in both languages.
    private const int PhoneWidth = 300;
    private const int PhoneHeight = 649;

    private static LandingImage Phone(string name, string altKey, string? altArgumentKey = null) =>
        new(name, PhoneWidth, PhoneHeight, altKey, altArgumentKey);

    private static LandingTheme Theme(string key, string swatch) =>
        new(key, $"Theme_{key}", swatch, Phone($"guest-{key}", nameof(LandingText.Theme_Alt), $"Theme_{key}"));

    /// <summary>The six themes in the switcher's order (key = <c>DesignTheme</c> in lower case, swatch = its accent).</summary>
    public static readonly IReadOnlyList<LandingTheme> Themes =
    [
        Theme("modern", "#0f766e"),
        Theme("classic", "#a14f5c"),
        Theme("elegant", "#8f6a0a"),
        Theme("minimal", "#1a1a1a"),
        Theme("fancy", "#c8336f"),
        Theme("dark", "#d27b90")
    ];

    /// <summary>
    /// The design the switcher shows first (not the hero's, so the page shows two designs right away). landing.css
    /// names it too, for browsers without :has().
    /// </summary>
    public const string DefaultThemeKey = "classic";

    /// <summary>The hero's phone: the demo menu with its restaurant profile.</summary>
    public static readonly LandingImage Hero = Phone("guest-modern", nameof(LandingText.Image_Hero));

    public static readonly LandingImage GuestItem = Phone("guest-item", nameof(LandingText.Image_GuestItem));

    public static readonly LandingImage GuestFilter = Phone("guest-filter", nameof(LandingText.Image_GuestFilter));

    public static readonly LandingImage GuestInfo = Phone("guest-info", nameof(LandingText.Image_GuestInfo));

    // Back office (desktop, cropped): shown in a browser frame, one set per language
    public static readonly LandingImage Calendar = new("qr-calendar", 640, 440, nameof(LandingText.Image_Calendar), PerLanguage: true);

    public static readonly LandingImage Editor = new("editor-preview", 640, 420, nameof(LandingText.Image_Editor), PerLanguage: true);

    public static readonly LandingImage Analytics = new("analytics", 640, 340, nameof(LandingText.Image_Analytics), PerLanguage: true);

    /// <summary>Every WebP image the page uses (each theme once).</summary>
    public static IEnumerable<LandingImage> All =>
        Themes.Select(t => t.Image)
            .Concat([GuestItem, GuestFilter, GuestInfo, Calendar, Editor, Analytics])
            .DistinctBy(i => i.Name);

    /// <summary>Open Graph image (link previews), 1200 × 630 PNG, with the headline in the language.</summary>
    public static string OpenGraphFor(string language) => $"{Folder}/og-{language}.png";

    public const int OpenGraphWidth = 1200;
    public const int OpenGraphHeight = 630;
}
