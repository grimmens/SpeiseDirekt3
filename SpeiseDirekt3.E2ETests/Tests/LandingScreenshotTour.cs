using FluentAssertions;
using System.Globalization;
using System.Text;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SpeiseDirekt.Model;
using SpeiseDirekt3.Components.Landing;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt3.E2ETests.Infrastructure;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Not a test in the usual sense: the tool that makes the landing page's product images from our own app, and screenshots
/// of the landing page for design reviews. Runs only when <c>E2E_SCREENSHOT_DIR</c> is set.
/// <para><see cref="Capture_landing_images"/> seeds the <see cref="GuestDemoData"/> menu ("Trattoria Lago") in all six
/// themes plus a holiday calendar per language and two weeks of scans, shoots the guest menu on a phone (390 × 844, 2x:
/// every theme, the item sheet, the filter sheet, the restaurant info sheet — the demo menu is German content, one set)
/// and the back office on a desktop in German and in English (2x: the menu editor with its phone frame, the QR
/// calendar in December, the analytics page — <c>…-de.webp</c> / <c>…-en.webp</c>), and writes every image of
/// <see cref="LandingImages"/> as WebP (1x and 2x, exactly the catalog's sizes) plus the Open Graph images
/// (<c>og-de.png</c> / <c>og-en.png</c>, 1200 × 630, the headline from LandingText, composed with the hero's phone
/// shot). The WebP files go to <c>E2E_LANDING_IMAGES_DIR</c>
/// (point it at <c>SpeiseDirekt3/wwwroot/images/landing</c> to update the page) or else to
/// <c>$E2E_SCREENSHOT_DIR/landing-images</c>; the raw PNGs to <c>$E2E_SCREENSHOT_DIR/landing-raw</c>. The back office
/// shots use the app's own css/tailwind.css, as in production.</para>
/// <para><see cref="Capture_landing_page"/> shoots the landing page at 360, 390, 768, 1024, 1280 and 1440 px (first screen
/// and full page, reduced motion so nothing waits for a fade-in), the open phone menu, the page without JS and a few
/// theme switches, in German and English (or only <c>E2E_LOCALE</c>), into <c>$E2E_SCREENSHOT_DIR/landing-page/{de|en}</c>.</para>
/// <para><see cref="Capture_imprint"/> (AP15a) shoots <c>/impressum</c> at 360, 390, 768 and 1280 px (first screen and full
/// page), with the e-mail placeholder and, at 390 and 1280 px, with a configured contact address, in German and English
/// (or only <c>E2E_LOCALE</c>), into <c>$E2E_SCREENSHOT_DIR/imprint/{de|en}</c>.</para>
/// <code>
/// E2E_SCREENSHOT_DIR=/tmp/shots E2E_LANDING_IMAGES_DIR=$PWD/SpeiseDirekt3/wwwroot/images/landing \
///   dotnet test SpeiseDirekt3.E2ETests --filter FullyQualifiedName~LandingScreenshotTour.Capture_landing_images
/// </code>
/// </summary>
public class LandingScreenshotTour : QrCalendarTestBase
{
    public const string ImagesDirectoryEnvVar = "E2E_LANDING_IMAGES_DIR";

    /// <summary>Where <see cref="Capture_demo_images"/> writes the live demo's photos (point it at <c>SpeiseDirekt3/wwwroot/images/demo</c>).</summary>
    public const string DemoImagesDirectoryEnvVar = "E2E_DEMO_IMAGES_DIR";

    public LandingScreenshotTour(E2EFixture fixture) : base(fixture) { }

    private static string? Directory => Environment.GetEnvironmentVariable(AdminScreenshotTour.DirectoryEnvVar) is { Length: > 0 } d ? d : null;

    // Back-office shots: a narrower desktop than 1280 px keeps the cropped text readable in the page's browser frame
    private const int DesktopWidth = 1024;

    [Fact]
    public async Task Capture_landing_images()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        var raw = Path.Combine(dir, "landing-raw");
        var output = Environment.GetEnvironmentVariable(ImagesDirectoryEnvVar) is { Length: > 0 } o ? o : Path.Combine(dir, "landing-images");
        System.IO.Directory.CreateDirectory(raw);
        System.IO.Directory.CreateDirectory(output);

        var ownerId = await RegisterOwnerAsync();
        var menus = new Dictionary<string, GuestDemoData.Seeded>();
        foreach (var theme in LandingImages.Themes)
            menus[theme.Key] = await GuestDemoData.SeedAsync(App, ownerId, Enum.Parse<DesignTheme>(theme.Key, ignoreCase: true));
        var modern = menus["modern"];
        var holidays = new Dictionary<string, (Guid QrId, int Year)>
        {
            ["de"] = await SeedHolidayCalendarAsync(ownerId, modern.MenuId, "Festtage (Kalender)", "Weihnachtsmenü", "Fischfreitag", "Silvester", "Mittagskarte"),
            ["en"] = await SeedHolidayCalendarAsync(ownerId, modern.MenuId, "Holidays (calendar)", "Christmas menu", "Fish Friday", "New Year's Eve", "Lunch menu")
        };
        await SeedScansAsync(ownerId, modern);

        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state, ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();

        async Task<string> PhoneShot(string name)
        {
            await SettleAsync(phone);
            var path = Path.Combine(raw, $"{name}.png");
            await phone.ScreenshotAsync(new() { Path = path });
            return path;
        }

        // --- The guest menu on a phone: every theme, then the sheets (modern) ---
        var shots = new Dictionary<string, string>();
        foreach (var theme in LandingImages.Themes)
        {
            await OpenGuestPageAsync(phone, $"/menu/{menus[theme.Key].MenuId}");
            shots[theme.Image.Name] = await PhoneShot(theme.Image.Name);
        }

        await OpenGuestPageAsync(phone, $"/menu/{modern.MenuId}");
        await ClickMenuItemAsync(phone, GuestDemoData.DishWithExtras);
        shots[LandingImages.GuestItem.Name] = await PhoneShot(LandingImages.GuestItem.Name);
        await CloseItemSheetAsync(phone);

        await phone.GetByTestId("filter-button").ClickAsync();
        var filter = phone.GetByTestId("filter-sheet");
        await filter.GetByTestId("filter-label-vegetarian").ClickAsync();
        await filter.Locator("input[data-filter-allergen='G']").CheckAsync();
        shots[LandingImages.GuestFilter.Name] = await PhoneShot(LandingImages.GuestFilter.Name);
        await phone.Keyboard.PressAsync("Escape");
        await Assertions.Expect(filter).ToBeHiddenAsync();

        await phone.EvaluateAsync("() => window.scrollTo(0, 0)");
        await phone.GetByTestId("info-button").ClickAsync();
        await Assertions.Expect(phone.GetByTestId("info-sheet")).ToBeVisibleAsync();
        shots[LandingImages.GuestInfo.Name] = await PhoneShot(LandingImages.GuestInfo.Name);
        await phone.Keyboard.PressAsync("Escape");

        // --- The back office on a desktop, once per language (cropped to the catalog's aspect ratio) ---
        foreach (var language in LandingImages.Languages)
        {
            await using var desktopContext = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress, StorageState = state, ReducedMotion = ReducedMotion.Reduce,
                ViewportSize = new ViewportSize { Width = DesktopWidth, Height = 900 }, DeviceScaleFactor = 2,
                Locale = language == "de" ? E2ESettings.GermanLocale : E2ESettings.DefaultLocale
            });
            desktopContext.SetDefaultTimeout(20_000);
            var desktop = await desktopContext.NewPageAsync();

            await GotoInteractiveAsync(desktop, $"/menu/edit/{modern.MenuId}");
            var preview = desktop.FrameLocator("[data-testid='menu-preview-iframe']");
            await preview.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            shots[$"{LandingImages.Editor.Name}-{language}"] = await CropShotAsync(desktop, desktop.Locator("div[class*='lg:grid-cols-2']").First,
                LandingImages.Editor, Path.Combine(raw, $"editor-preview-{language}.png"), padding: 16);

            var (holidayQr, holidayYear) = holidays[language];
            await GotoInteractiveAsync(desktop, $"/qrcode/edit/{holidayQr}");
            await WaitForCalendarAsync(desktop);
            await GotoMonthAsync(desktop, holidayYear, 12);
            shots[$"{LandingImages.Calendar.Name}-{language}"] = await CropShotAsync(desktop, CalendarOf(desktop),
                LandingImages.Calendar, Path.Combine(raw, $"qr-calendar-{language}.png"), padding: 12);

            await GotoInteractiveAsync(desktop, "/analytics");
            await Assertions.Expect(desktop.GetByTestId("analytics-summary")).ToBeVisibleAsync();
            await desktop.WaitForFunctionAsync("() => document.querySelectorAll('canvas').length > 0");
            shots[$"{LandingImages.Analytics.Name}-{language}"] = await CropShotAsync(desktop, desktop.GetByTestId("analytics-summary"),
                LandingImages.Analytics, Path.Combine(raw, $"analytics-{language}.png"), padding: 16);
        }

        // --- WebP 1x/2x in the catalog's sizes (a per-language image once per language) ---
        var report = new StringBuilder();
        foreach (var (image, language) in LandingImages.All.SelectMany(i =>
                     i.PerLanguage ? LandingImages.Languages.Select(l => (i, l)) : [(i, LandingImages.Languages[0])]))
        {
            using var source = await ImageSharpImage.LoadAsync(shots[image.PerLanguage ? $"{image.Name}-{language}" : image.Name]);
            foreach (var scale in new[] { 1, 2 })
            {
                using var resized = source.Clone(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(image.Width * scale, image.Height * scale),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Lanczos3
                }));
                var path = Path.Combine(output, Path.GetFileName(image.PathFor(scale, language)));
                await resized.SaveAsWebpAsync(path, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = scale == 1 ? 82 : 74,
                    Method = WebpEncodingMethod.BestQuality,
                    SkipMetadata = true
                });
                report.AppendLine(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}\t{image.Width * scale}x{image.Height * scale}\t{new FileInfo(path).Length / 1024.0:0.0} KB");
            }
        }

        // --- Open Graph images, one per language ---
        foreach (var language in LandingImages.Languages)
        {
            var og = Path.Combine(output, Path.GetFileName(LandingImages.OpenGraphFor(language)));
            await ComposeOpenGraphAsync(shots[LandingImages.Hero.Name], og, language);
            report.AppendLine(CultureInfo.InvariantCulture, $"{Path.GetFileName(og)}\t{LandingImages.OpenGraphWidth}x{LandingImages.OpenGraphHeight}\t{new FileInfo(og).Length / 1024.0:0.0} KB");
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "landing-images.txt"), report.ToString());
    }

    /// <summary>
    /// The live demo's photos (<c>/demo</c>, <c>Components/Demo</c>): the same generated pictures the
    /// <see cref="GuestDemoData"/> seed stores in the database — every dish photo (600 × 750), the cover (1200 × 600) and
    /// the logo (320 × 320) — as WebP <c>{stem}.webp</c> into <c>E2E_DEMO_IMAGES_DIR</c>. Opt-in; needs no browser:
    /// <code>
    /// E2E_DEMO_IMAGES_DIR=$PWD/SpeiseDirekt3/wwwroot/images/demo \
    ///   dotnet test SpeiseDirekt3.E2ETests --filter FullyQualifiedName~Capture_demo_images
    /// </code>
    /// Then rebuild (fingerprinted URLs) and commit the files (<c>*.webp</c> is binary in .gitattributes).
    /// </summary>
    [Fact]
    public async Task Capture_demo_images()
    {
        if (Environment.GetEnvironmentVariable(DemoImagesDirectoryEnvVar) is not { Length: > 0 } output) return; // opt-in only
        System.IO.Directory.CreateDirectory(output);
        var sizes = new Dictionary<string, Size>
        {
            [SpeiseDirekt3.Components.Demo.DemoMenuContent.CoverPhoto] = new(1200, 600),
            [SpeiseDirekt3.Components.Demo.DemoMenuContent.LogoPhoto] = new(320, 320)
        };
        foreach (var (stem, image) in GuestDemoData.DemoPhotos())
        {
            using (image)
            {
                if (sizes.TryGetValue(stem, out var size))
                    image.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
                await image.SaveAsWebpAsync(Path.Combine(output, stem + ".webp"), new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = 78,
                    Method = WebpEncodingMethod.BestQuality,
                    SkipMetadata = true
                });
            }
        }
        SpeiseDirekt3.Components.Demo.DemoMenuContent.Photos
            .Should().OnlyContain(stem => File.Exists(Path.Combine(output, stem + ".webp")), "every photo the demo names exists");
    }

    [Fact]
    public async Task Capture_landing_page()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        var locales = Environment.GetEnvironmentVariable(E2ESettings.LocaleEnvVar) is { Length: > 0 } only
            ? new[] { only }
            : new[] { E2ESettings.GermanLocale, E2ESettings.DefaultLocale };
        foreach (var locale in locales)
            await CaptureLandingPageAsync(Path.Combine(dir, "landing-page", locale[..2]), locale);
    }

    [Fact]
    public async Task Capture_imprint()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        var locales = Environment.GetEnvironmentVariable(E2ESettings.LocaleEnvVar) is { Length: > 0 } only
            ? new[] { only }
            : new[] { E2ESettings.GermanLocale, E2ESettings.DefaultLocale };
        var configuration = App.ServerServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        foreach (var locale in locales)
        {
            var target = Path.Combine(dir, "imprint", locale[..2]);
            System.IO.Directory.CreateDirectory(target);
            foreach (var (width, height, withEmail) in new[] { (360, 780, false), (390, 844, false), (768, 1024, false), (1280, 800, false), (390, 844, true), (1280, 800, true) })
            {
                var mobile = width < 1024;
                configuration["Landing:ContactEmail"] = withEmail ? "kontakt@speisedirekt.example" : "";
                try
                {
                    await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
                    {
                        BaseURL = App.ServerAddress, ReducedMotion = ReducedMotion.Reduce, Locale = locale,
                        ViewportSize = new ViewportSize { Width = width, Height = height }, DeviceScaleFactor = mobile ? 2 : 1,
                        IsMobile = mobile, HasTouch = mobile
                    });
                    var page = await context.NewPageAsync();
                    await page.GotoAsync("/impressum");
                    await page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
                    await SettleAsync(page);
                    var name = $"{width}{(withEmail ? "-email" : "")}";
                    await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{name}-top.png") });
                    await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{name}-full.png"), FullPage = true });
                }
                finally
                {
                    configuration["Landing:ContactEmail"] = "";
                }
            }
        }
    }

    private async Task CaptureLandingPageAsync(string target, string locale)
    {
        System.IO.Directory.CreateDirectory(target);

        foreach (var (width, height, scale) in new[] { (360, 780, 2), (390, 844, 2), (768, 1024, 2), (1024, 768, 1), (1280, 800, 1), (1440, 900, 1) })
        {
            var mobile = width < 1024;
            await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress, ReducedMotion = ReducedMotion.Reduce, Locale = locale,
                ViewportSize = new ViewportSize { Width = width, Height = height }, DeviceScaleFactor = scale, IsMobile = mobile, HasTouch = mobile
            });
            var page = await context.NewPageAsync();
            await page.GotoAsync("/");
            await page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await LoadAllImagesAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{width}-top.png") });
            await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{width}-full.png"), FullPage = true });

            if (width is 390 or 1280)
            {
                await page.GetByTestId("landing-designs").ScrollIntoViewIfNeededAsync();
                await page.Locator("label[for='lp-theme-dark']").ClickAsync();
                await SettleAsync(page);
                await page.GetByTestId("landing-designs").ScreenshotAsync(new() { Path = Path.Combine(target, $"{width}-designs-dark.png") });
                await page.EvaluateAsync("() => window.scrollTo(0, 1400)");
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{width}-scrolled.png") });
            }
            if (width is 390 or 768)
            {
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                await page.GetByTestId("landing-menu-button").ClickAsync();
                await Assertions.Expect(page.GetByTestId("landing-menu")).ToBeVisibleAsync();
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{width}-menu-open.png") });
            }
        }

        // Without JavaScript (390 px, full page)
        await using var noJs = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false, Locale = locale,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        });
        var plain = await noJs.NewPageAsync();
        await plain.GotoAsync("/");
        await LoadAllImagesAsync(plain);
        await plain.ScreenshotAsync(new() { Path = Path.Combine(target, "390-nojs-full.png"), FullPage = true });
    }

    /// <summary>Scrolls through the page so every lazy image loads, then back to the top.</summary>
    private static async Task LoadAllImagesAsync(IPage page)
    {
        await page.EvaluateAsync("""
            async () => {
                for (const img of document.images) img.loading = 'eager';
                await Promise.all([...document.images].filter(i => i.checkVisibility()).map(i => i.decode().catch(() => {})));
                await document.fonts.ready;
            }
            """);
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task SettleAsync(IPage page)
    {
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5_000 }); } catch (TimeoutException) { }
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>
    /// A screenshot of the page area that starts <paramref name="padding"/> px above and left of <paramref name="anchor"/>,
    /// as wide as the anchor plus the padding on both sides, and as high as the image's aspect ratio asks for.
    /// </summary>
    private static async Task<string> CropShotAsync(IPage page, ILocator anchor, LandingImage image, string path, int padding)
    {
        // No focus ring of the last click (e.g. the calendar's "next month" button) in the picture
        await page.EvaluateAsync("() => { document.activeElement?.blur(); window.scrollTo(0, 0); }");
        await SettleAsync(page);
        var box = await anchor.EvaluateAsync<double[]>("e => { const r = e.getBoundingClientRect(); return [r.left + scrollX, r.top + scrollY, r.width]; }");
        var x = Math.Max(0, box[0] - padding);
        var y = Math.Max(0, box[1] - padding);
        var width = Math.Min(box[2] + 2 * padding, DesktopWidth - x);
        var height = width * image.Height / image.Width;
        await page.ScreenshotAsync(new() { Path = path, FullPage = true, Clip = new Clip { X = (float)x, Y = (float)y, Width = (float)width, Height = (float)height } });
        return path;
    }

    /// <summary>
    /// The Open Graph image of <paramref name="language"/>: brand stage with the logo, the headline (LandingText
    /// <c>Og_Headline</c>/<c>Og_Text</c>) and the hero's phone. An HTML composition rendered on the app's origin (fonts,
    /// brand tokens and logo come from the app), saved as PNG.
    /// </summary>
    private async Task ComposeOpenGraphAsync(string phoneShot, string path, string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        string Text(string key) => System.Net.WebUtility.HtmlEncode(SpeiseDirekt3.LandingText.ResourceManager.GetString(key, culture)!);
        var headline = Text("Og_Headline").Replace("[", "<mark>").Replace("]", "</mark>");
        var text = Text("Og_Text");
        await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress,
            ViewportSize = new ViewportSize { Width = LandingImages.OpenGraphWidth, Height = LandingImages.OpenGraphHeight },
            DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/"); // any page of the app: the composition's URLs resolve against its origin
        var phone = Convert.ToBase64String(await File.ReadAllBytesAsync(phoneShot));
        await page.SetContentAsync($$"""
            <!DOCTYPE html>
            <html lang="{{language}}"><head>
            <link rel="stylesheet" href="/css/fonts.css"><link rel="stylesheet" href="/css/brand.css">
            <style>
              * { box-sizing: border-box; margin: 0; }
              body { width: 1200px; height: 630px; overflow: hidden; font-family: var(--sd-font-sans); color: #fff;
                background: radial-gradient(80% 90% at 85% 0%, rgb(var(--sd-brand-600)) 0%, rgb(var(--sd-brand-900)) 55%, rgb(var(--sd-brand-950)) 100%); position: relative; }
              .dots { position: absolute; inset: 0; background-image: radial-gradient(rgb(255 255 255 / .12) 1px, transparent 1.6px);
                background-size: 22px 22px; -webkit-mask-image: radial-gradient(60% 70% at 75% 50%, #000, transparent); }
              .glow { position: absolute; width: 560px; height: 560px; right: -140px; bottom: -260px; border-radius: 50%;
                background: radial-gradient(circle, rgb(var(--sd-accent-400) / .4), transparent 66%); }
              .copy { position: absolute; left: 72px; top: 72px; width: 640px; }
              .copy img { height: 46px; width: auto; display: block; }
              h1 { margin-top: 64px; font: 800 66px/1.04 var(--sd-font-display); letter-spacing: -.035em; }
              mark { color: rgb(var(--sd-brand-950)); background: linear-gradient(transparent 8%, rgb(var(--sd-accent-300)) 8%, rgb(var(--sd-accent-300)) 92%, transparent 92%);
                padding: 0 .1em; -webkit-box-decoration-break: clone; box-decoration-break: clone; }
              p { margin-top: 30px; font-size: 26px; line-height: 1.45; color: rgb(255 255 255 / .82); }
              .phone-wrap { position: absolute; right: 96px; top: 64px; padding: 18px; }
              .phone { width: 300px; padding: 9px; border-radius: 42px; background: linear-gradient(150deg, #33464d, #0d1a1e 45%, #050b0d);
                box-shadow: inset 0 0 0 1px rgb(255 255 255 / .1), 0 40px 70px -30px rgb(0 0 0 / .6); }
              .phone img { display: block; width: 100%; border-radius: 34px; }
              .corner { position: absolute; width: 40px; height: 40px; border: 5px solid rgb(var(--sd-accent-400)); }
              .tl { top: 0; left: 0; border-right: 0; border-bottom: 0; border-top-left-radius: 18px; }
              .tr { top: 0; right: 0; border-left: 0; border-bottom: 0; border-top-right-radius: 18px; }
            </style></head>
            <body>
              <div class="dots"></div><div class="glow"></div>
              <div class="copy">
                <img src="/images/brand/logo-inverse.svg" alt="">
                <h1>{{headline}}</h1>
                <p>{{text}}</p>
              </div>
              <div class="phone-wrap"><span class="corner tl"></span><span class="corner tr"></span>
                <div class="phone"><img src="data:image/png;base64,{{phone}}" alt=""></div></div>
            </body></html>
            """);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await Promise.all([...document.images].map(i => i.decode())); }");
        var png = await page.ScreenshotAsync(new() { Type = ScreenshotType.Png });
        using var image = ImageSharpImage.Load(png);
        await image.SaveAsPngAsync(path, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression, SkipMetadata = true });
    }

    /// <summary>
    /// A QR code (<paramref name="title"/>, e.g. "Festtage (Kalender)") with the fallback menu
    /// <paramref name="fallbackMenuId"/>: the Christmas menu from the 24th to the 26th of December, a weekly fish Friday
    /// from November into January, New Year's Eve; plus a lunch menu — the menus named in the shot's language. Returns the
    /// code and the year of the December shown.
    /// </summary>
    private async Task<(Guid QrId, int Year)> SeedHolidayCalendarAsync(Guid ownerId, Guid fallbackMenuId, string title,
        string christmasName, string fishName, string newYearName, string lunchName)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var year = today.Month == 12 && today.Day > 26 ? today.Year + 1 : today.Year;
        var firstFriday = new DateOnly(year, 11, 1);
        while (firstFriday.DayOfWeek != DayOfWeek.Friday) firstFriday = firstFriday.AddDays(1);
        var lastFriday = new DateOnly(year + 1, 1, 31);
        while (lastFriday.DayOfWeek != DayOfWeek.Friday) lastFriday = lastFriday.AddDays(-1);

        var christmas = await SeedMenuAsync(ownerId, christmasName);
        var fish = await SeedMenuAsync(ownerId, fishName);
        var newYear = await SeedMenuAsync(ownerId, newYearName);
        await SeedMenuAsync(ownerId, lunchName);
        var qr = await SeedQrAsync(ownerId, new QRCode
        {
            Title = title, IsCalendarBased = true, MenuId = fallbackMenuId,
            CalendarEntries = new List<CalendarEntry>
            {
                Calendar(firstFriday, fish, until: lastFriday, weekly: DayOfWeek.Friday),
                Calendar(new DateOnly(year, 12, 24), christmas, until: new DateOnly(year, 12, 26)),
                Calendar(new DateOnly(year, 12, 31), newYear)
            }
        });
        return (qr, year);
    }

    /// <summary>Two weeks of QR scans, item clicks and sessions on the demo menu (the analytics page).</summary>
    private async Task SeedScansAsync(Guid ownerId, GuestDemoData.Seeded menu)
    {
        var qr = await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1", MenuId = menu.MenuId });
        var dishes = menu.Items.Values.ToArray();
        await App.SeedAsync(ownerId, db =>
        {
            var random = new Random(7);
            for (var day = 0; day < 14; day++)
            {
                var date = DateTime.UtcNow.Date.AddDays(-day);
                var visits = 14 + random.Next(16) + (date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 12 : 0);
                for (var i = 0; i < visits; i++)
                {
                    var session = Guid.NewGuid().ToString("N");
                    var at = date.AddHours(11 + random.Next(11)).AddMinutes(random.Next(60));
                    db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = session, MenuId = menu.MenuId, QRCodeId = qr, ViewedAt = at });
                    for (var c = 0; c < random.Next(4); c++)
                        db.MenuItemClicks.Add(new MenuItemClick
                        {
                            Id = Guid.NewGuid(), SessionId = session, MenuId = menu.MenuId,
                            MenuItemId = dishes[random.Next(dishes.Length)], ClickedAt = at.AddMinutes(1 + c)
                        });
                }
            }
            return Task.CompletedTask;
        });
    }
}
