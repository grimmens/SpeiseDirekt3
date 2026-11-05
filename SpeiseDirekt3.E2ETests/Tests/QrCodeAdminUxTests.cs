using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Back-office QR codes (package P3): the calendar mode of the editor (<c>/qrcode/edit</c>), the list cards on
/// <c>/qrcodes</c> (mode badge, target text, guest URL, Download PNG, Copy link) and the editor preview.
/// Every test registers its own owner and seeds its menus directly.
/// </summary>
public class QrCodeAdminUxTests : BackOfficeTestBase
{
    public QrCodeAdminUxTests(E2EFixture fixture) : base(fixture) { }

    // --- Helpers ---

    /// <summary>Registers a fresh owner in <see cref="E2ETestBase.Page"/> and returns its tenant id.</summary>
    private async Task<Guid> RegisterOwnerAsync()
    {
        var email = await RegisterNewOwnerAsync();
        await using var db = App.CreateDbContext(Guid.Empty);
        return Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
    }

    /// <summary>A menu with one category and one item ("{name} Gericht"), so the guest page renders it.</summary>
    private async Task<Guid> SeedMenuAsync(Guid ownerId, string name)
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = $"{name} Kategorie", MenuId = menuId };
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = name, Description = "", Language = MenuLanguage.German,
                Categories = new List<Category> { category }
            });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = $"{name} Gericht", Description = "Test", Price = 9.90m, CategoryId = category.Id });
            return Task.CompletedTask;
        });
        return menuId;
    }

    private async Task<Guid> SeedQrAsync(Guid ownerId, QRCode qr)
    {
        qr.Id = qr.Id == Guid.Empty ? Guid.NewGuid() : qr.Id;
        foreach (var e in qr.TimeTableEntries ?? new()) e.QRCodeId = qr.Id;
        foreach (var e in qr.CalendarEntries ?? new()) e.QRCodeId = qr.Id;
        await App.SeedAsync(ownerId, db => { db.QRCodes.Add(qr); return Task.CompletedTask; });
        return qr.Id;
    }

    private static CalendarEntry Calendar(DateOnly date, Guid menuId, DateOnly? until = null, DayOfWeek? weekly = null) =>
        new() { Id = Guid.NewGuid(), Date = date, EndDate = until, RecurringDayOfWeek = weekly, MenuId = menuId };

    private async Task<QRCode> LoadQrAsync(Guid ownerId, Guid qrId)
    {
        await using var db = App.CreateDbContext(ownerId);
        return await db.QRCodes.AsNoTracking()
            .Include(q => q.CalendarEntries)
            .Include(q => q.TimeTableEntries)
            .SingleAsync(q => q.Id == qrId);
    }

    private ILocator Card(string title) =>
        Page.GetByTestId("qr-card").Filter(new() { Has = Page.Locator("h3", new() { HasTextString = title }) });

    private static async Task ExpectGuestSeesMenuAsync(IPage guest, Guid qrId, string menuName)
    {
        await OpenPublicMenuPageAsync(guest, $"/qr/{qrId}");
        await Assertions.Expect(guest.Locator("h1").First).ToHaveTextAsync(menuName);
        await Assertions.Expect(guest.Locator("h3", new() { HasTextString = $"{menuName} Gericht" }).First).ToBeVisibleAsync();
    }

    private static string DateValue(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <summary>
    /// Picks "Calendar-based Menu". The title field commits on blur, i.e. when the radio is clicked; the
    /// re-render for the title briefly re-checks the old radio until the mode change arrives, so wait for
    /// the final state instead of using CheckAsync (which inspects the radio right after the click).
    /// </summary>
    private async Task SelectCalendarModeAsync()
    {
        await Page.Locator("#calendar").ClickAsync();
        await Assertions.Expect(Page.Locator("#calendar")).ToBeCheckedAsync();
        await Assertions.Expect(Button("Add Calendar Entry")).ToBeVisibleAsync();
    }

    // --- Editor: calendar mode ---

    [Fact]
    public async Task CalendarQr_CreatedInEditor_ShowsEntryMenuOnItsDate_AndFallbackMenuOtherwise()
    {
        var ownerId = await RegisterOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync(ownerId, $"Tageskarte {suffix}");
        var christmas = await SeedMenuAsync(ownerId, $"Weihnachten {suffix}");
        // The owner works "today" at a fixed time: a clock far ahead would expire the auth cookie
        // (cookie auth uses the same TimeProvider). Only the anonymous guest travels to the entry's date.
        var today = DateOnly.FromDateTime(DateTime.Now);
        App.Clock.SetLocalNow(today.ToDateTime(new TimeOnly(12, 0)));

        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Weihnachtsaktion");
        await SelectCalendarModeAsync();
        await Assertions.Expect(Page.GetByText("No calendar entries yet")).ToBeVisibleAsync();
        await Button("Add Calendar Entry").ClickAsync();

        var entry = Page.GetByTestId("calendar-entry");
        await Assertions.Expect(entry).ToHaveCountAsync(1);
        await Assertions.Expect(entry.Locator("#cal-0-date")).ToHaveValueAsync(DateValue(today)); // defaults to today
        await entry.Locator("#cal-0-menu").SelectOptionAsync(new SelectOptionValue { Label = $"Weihnachten {suffix}" });
        await entry.Locator("#cal-0-date").FillAsync("2026-12-24");
        await Assertions.Expect(entry.GetByTestId("calendar-entry-summary")).ToHaveTextAsync("Only on Thu, Dec 24, 2026");

        await Page.Locator("#fallbackMenuSelect").SelectOptionAsync(new SelectOptionValue { Label = $"Tageskarte {suffix}" });
        await Assertions.Expect(Page.GetByText("Shown when no calendar entry matches today")).ToBeVisibleAsync();
        await Button("Create QR Code").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        var card = Card("Weihnachtsaktion");
        await Assertions.Expect(card.GetByTestId("qr-mode")).ToHaveTextAsync("Calendar");
        await Assertions.Expect(card.GetByTestId("qr-target")).ToHaveTextAsync($"1 entry, otherwise Tageskarte {suffix}");
        var href = await card.Locator("a[href^='qr/']").GetAttributeAsync("href");
        var qrId = Guid.Parse(href!["qr/".Length..]);

        var saved = await LoadQrAsync(ownerId, qrId);
        saved.IsCalendarBased.Should().BeTrue();
        saved.IsTimeTableBased.Should().BeFalse();
        saved.MenuId.Should().Be(regular);
        saved.CalendarEntries.Should().ContainSingle().Which.Should().Match<CalendarEntry>(e =>
            e.Date == new DateOnly(2026, 12, 24) && e.MenuId == christmas && e.RecurringDayOfWeek == null
            && e.EndDate == null && e.ApplicationUserId == ownerId);

        // Anonymous guests (fresh contexts, so the menu script is fetched again) on the entry's date and the day after
        foreach (var (day, menuName) in new[] { (24, $"Weihnachten {suffix}"), (25, $"Tageskarte {suffix}") })
        {
            App.Clock.SetLocalNow(new DateTime(2026, 12, day, 12, 0, 0));
            var (guestContext, guest) = await NewContextAsync();
            await using var _ = guestContext;
            await ExpectGuestSeesMenuAsync(guest, qrId, menuName);
        }
    }

    [Fact]
    public async Task EditingCalendarQr_PreselectsModeAndEntries_AndSavingKeepsThem()
    {
        var ownerId = await RegisterOwnerAsync();
        var regular = await SeedMenuAsync(ownerId, "Tageskarte");
        var christmas = await SeedMenuAsync(ownerId, "Weihnachten");
        var friday = await SeedMenuAsync(ownerId, "Fischfreitag");
        var single = Calendar(new DateOnly(2026, 12, 24), christmas);
        var weekly = Calendar(new DateOnly(2027, 1, 1), friday, until: new DateOnly(2027, 3, 26), weekly: DayOfWeek.Friday);
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Kalender Tisch", IsCalendarBased = true, MenuId = regular,
            CalendarEntries = new List<CalendarEntry> { weekly, single }
        });

        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");

        await Assertions.Expect(Page.Locator("#calendar")).ToBeCheckedAsync();
        await Assertions.Expect(Page.Locator("#simple")).Not.ToBeCheckedAsync();
        await Assertions.Expect(Page.Locator("#fallbackMenuSelect")).ToHaveValueAsync(regular.ToString());
        var entries = Page.GetByTestId("calendar-entry");
        await Assertions.Expect(entries).ToHaveCountAsync(2);

        // Sorted by date: the single date first
        await Assertions.Expect(Page.Locator("#cal-0-menu")).ToHaveValueAsync(christmas.ToString());
        await Assertions.Expect(Page.Locator("#cal-0-date")).ToHaveValueAsync("2026-12-24");
        await Assertions.Expect(Page.Locator("#cal-0-weekly")).ToHaveValueAsync("");
        await Assertions.Expect(Page.Locator("#cal-0-until")).ToHaveValueAsync("");
        await Assertions.Expect(Page.Locator("#cal-1-menu")).ToHaveValueAsync(friday.ToString());
        await Assertions.Expect(Page.Locator("#cal-1-date")).ToHaveValueAsync("2027-01-01");
        await Assertions.Expect(Page.Locator("#cal-1-weekly")).ToHaveValueAsync("Friday");
        await Assertions.Expect(Page.Locator("#cal-1-until")).ToHaveValueAsync("2027-03-26");
        await Assertions.Expect(entries.Nth(1).GetByTestId("calendar-entry-summary"))
            .ToHaveTextAsync("Every Friday from Fri, Jan 1, 2027 until Fri, Mar 26, 2027");

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));

        var saved = await LoadQrAsync(ownerId, qrId);
        saved.IsCalendarBased.Should().BeTrue();
        saved.IsTimeTableBased.Should().BeFalse();
        saved.MenuId.Should().Be(regular);
        saved.CalendarEntries.Should().HaveCount(2);
        saved.CalendarEntries.Should().ContainSingle(e => e.Id == single.Id && e.Date == single.Date && e.MenuId == christmas
            && e.RecurringDayOfWeek == null && e.EndDate == null);
        saved.CalendarEntries.Should().ContainSingle(e => e.Id == weekly.Id && e.Date == weekly.Date && e.MenuId == friday
            && e.RecurringDayOfWeek == DayOfWeek.Friday && e.EndDate == new DateOnly(2027, 3, 26));
    }

    [Fact]
    public async Task EditingCalendarQr_ChangedAndRemovedEntries_AreSaved()
    {
        var ownerId = await RegisterOwnerAsync();
        var regular = await SeedMenuAsync(ownerId, "Tageskarte");
        var christmas = await SeedMenuAsync(ownerId, "Weihnachten");
        var monday = await SeedMenuAsync(ownerId, "Montagsmenü");
        var removed = Calendar(new DateOnly(2026, 12, 24), christmas);
        var kept = Calendar(new DateOnly(2027, 1, 4), christmas);
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Kalender Bar", IsCalendarBased = true, MenuId = regular,
            CalendarEntries = new List<CalendarEntry> { removed, kept }
        });

        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");
        var entries = Page.GetByTestId("calendar-entry");
        await Assertions.Expect(entries).ToHaveCountAsync(2);

        await entries.Nth(0).GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Assertions.Expect(entries).ToHaveCountAsync(1);
        await Page.Locator("#cal-0-menu").SelectOptionAsync(new SelectOptionValue { Label = "Montagsmenü" });
        await Page.Locator("#cal-0-weekly").SelectOptionAsync("Monday");
        await Page.Locator("#cal-0-until").FillAsync("2027-02-22");
        await Assertions.Expect(entries.First.GetByTestId("calendar-entry-summary"))
            .ToHaveTextAsync("Every Monday from Mon, Jan 4, 2027 until Mon, Feb 22, 2027");

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));

        var saved = await LoadQrAsync(ownerId, qrId);
        saved.IsCalendarBased.Should().BeTrue();
        saved.CalendarEntries.Should().ContainSingle().Which.Should().Match<CalendarEntry>(e =>
            e.Id == kept.Id && e.MenuId == monday && e.RecurringDayOfWeek == DayOfWeek.Monday && e.EndDate == new DateOnly(2027, 2, 22));
        await using var db = App.CreateDbContext(ownerId);
        (await db.CalendarEntries.AnyAsync(e => e.Id == removed.Id)).Should().BeFalse("a removed entry is deleted, not orphaned");
    }

    [Fact]
    public async Task CalendarEditor_ShowsValidationMessages()
    {
        await RegisterOwnerAsync();

        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Ungültig");
        await SelectCalendarModeAsync();

        // No entry at all
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page.GetByText("Es muss zumindest einen Kalender-Eintrag geben.").First).ToBeVisibleAsync();

        // An entry without a menu
        await Button("Add Calendar Entry").ClickAsync();
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page.GetByText("Ein Kalender-Eintrag muss ein Menü auswählen.").First).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcode/edit$"));
    }

    [Fact]
    public async Task CalendarEditor_SingleDateInsideWeeklyPeriod_CanBeSaved()
    {
        // A single date beats a weekly entry on the same day (MenuService), so this is not an overlap
        var ownerId = await RegisterOwnerAsync();
        var regular = await SeedMenuAsync(ownerId, "Tageskarte");
        var special = await SeedMenuAsync(ownerId, "Tagesaktion");
        var friday = await SeedMenuAsync(ownerId, "Fischfreitag");
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Aktion und Freitag", IsCalendarBased = true, MenuId = regular,
            CalendarEntries = new List<CalendarEntry>
            {
                Calendar(new DateOnly(2026, 11, 6), special),
                Calendar(new DateOnly(2026, 11, 6), friday, until: new DateOnly(2027, 1, 29), weekly: DayOfWeek.Friday)
            }
        });

        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");
        await Button("Save Changes").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        (await LoadQrAsync(ownerId, qrId)).CalendarEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task CalendarEditor_RejectsOverlappingEntriesOfTheSameKind()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await SeedMenuAsync(ownerId, "Überschneidung");

        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Doppelt");
        await SelectCalendarModeAsync();
        await Button("Add Calendar Entry").ClickAsync();
        await Button("Add Calendar Entry").ClickAsync();
        foreach (var i in new[] { 0, 1 })
            await Page.Locator($"#cal-{i}-menu").SelectOptionAsync(menu.ToString());

        // Two weekly entries on Monday without an end date overlap forever
        await Page.Locator("#cal-0-date").FillAsync("2026-11-09");
        await Page.Locator("#cal-0-weekly").SelectOptionAsync("Monday");
        await Page.Locator("#cal-1-weekly").SelectOptionAsync("Monday");
        await Page.Locator("#cal-1-date").FillAsync("2026-12-07");
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page.GetByText("Die wöchentlichen Einträge am Montag überschneiden sich").First).ToBeVisibleAsync();

        // On different weekdays they do not
        await Page.Locator("#cal-1-weekly").SelectOptionAsync("Tuesday");
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));

        // Two date ranges that share a day are still rejected
        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Doppelte Woche");
        await SelectCalendarModeAsync();
        await Button("Add Calendar Entry").ClickAsync();
        await Button("Add Calendar Entry").ClickAsync();
        foreach (var i in new[] { 0, 1 })
            await Page.Locator($"#cal-{i}-menu").SelectOptionAsync(menu.ToString());
        await Page.Locator("#cal-0-date").FillAsync("2026-11-09");
        await Page.Locator("#cal-0-until").FillAsync("2026-11-12");
        await Page.Locator("#cal-1-date").FillAsync("2026-11-12");
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page.GetByText("Die Datumsperioden überschneiden sich").First).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcode/edit$"));
    }

    // --- List cards ---

    [Fact]
    public async Task List_ShowsModeBadgeAndWhatGuestsGet()
    {
        var ownerId = await RegisterOwnerAsync();
        var main = await SeedMenuAsync(ownerId, "Gasthaus Zur Post");
        var lunch = await SeedMenuAsync(ownerId, "Mittagskarte");
        var direct = await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1", MenuId = main });
        await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Eingang", IsTimeTableBased = true,
            TimeTableEntries = new List<TimeTableEntry>
            {
                new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(22, 0), MenuId = main },
                new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(14, 0), MenuId = lunch }
            }
        });
        await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Aktion", IsCalendarBased = true, MenuId = main,
            CalendarEntries = new List<CalendarEntry>
            {
                Calendar(new DateOnly(2026, 12, 24), lunch),
                Calendar(new DateOnly(2026, 12, 31), lunch),
                Calendar(new DateOnly(2027, 1, 4), lunch, weekly: DayOfWeek.Monday)
            }
        });
        await SeedQrAsync(ownerId, new QRCode { Title = "Ohne Menü" });

        await GotoInteractiveAsync("/qrcodes");

        await Assertions.Expect(Page.GetByTestId("qr-card")).ToHaveCountAsync(4);
        await Assertions.Expect(Card("Tisch 1").GetByTestId("qr-mode")).ToHaveTextAsync("Direct");
        await Assertions.Expect(Card("Tisch 1").GetByTestId("qr-target")).ToHaveTextAsync("Gasthaus Zur Post");
        await Assertions.Expect(Card("Eingang").GetByTestId("qr-mode")).ToHaveTextAsync("Schedule");
        await Assertions.Expect(Card("Eingang").GetByTestId("qr-target"))
            .ToHaveTextAsync("11:00–14:00 Mittagskarte · 17:00–22:00 Gasthaus Zur Post");
        await Assertions.Expect(Card("Aktion").GetByTestId("qr-mode")).ToHaveTextAsync("Calendar");
        await Assertions.Expect(Card("Aktion").GetByTestId("qr-target")).ToHaveTextAsync("3 entries, otherwise Gasthaus Zur Post");
        await Assertions.Expect(Card("Ohne Menü").GetByTestId("qr-target")).ToHaveTextAsync("No menu selected");

        // The guest URL is shown as text; the title still links to the guest page
        await Assertions.Expect(Card("Tisch 1").GetByTestId("qr-url")).ToHaveTextAsync($"{App.ServerAddress.TrimEnd('/')}/qr/{direct}");
        await Assertions.Expect(Card("Tisch 1").Locator("a[href^='qr/']")).ToHaveAttributeAsync("href", $"qr/{direct}");
    }

    [Fact]
    public async Task List_DownloadPng_IsAnAttachmentNamedAfterTheTitle()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await SeedMenuAsync(ownerId, "Download Menü");
        var qrId = await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1 – Terrasse", MenuId = menu });

        await GotoInteractiveAsync("/qrcodes");
        var link = Card("Tisch 1 – Terrasse").GetByTestId("qr-download");
        await Assertions.Expect(link).ToHaveTextAsync("Download PNG");
        await Assertions.Expect(link).ToHaveAttributeAsync("download", "qr-tisch-1-terrasse.png");
        var href = await link.GetAttributeAsync("href");
        href.Should().StartWith($"Image/qr/{qrId}?download=1");

        var response = await Page.APIRequest.GetAsync(href!);
        response.Status.Should().Be(200);
        response.Headers["content-type"].Should().Be("image/png");
        response.Headers["content-disposition"].Should().Contain("attachment").And.Contain("qr-tisch-1-terrasse.png");
        (await response.BodyAsync()).Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG signature

        // A click downloads the file (Blazor does not intercept links with a download attribute)
        var download = await Page.RunAndWaitForDownloadAsync(() => link.ClickAsync());
        download.SuggestedFilename.Should().Be("qr-tisch-1-terrasse.png");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
    }

    [Fact]
    public async Task ImageEndpoint_WithoutDownload_IsInline_AndRejectsUnsafeNames()
    {
        var qrId = Guid.NewGuid();

        var inline = await Page.APIRequest.GetAsync($"Image/qr/{qrId}");
        inline.Status.Should().Be(200);
        inline.Headers["content-type"].Should().Be("image/png");
        inline.Headers.Should().NotContainKey("content-disposition");

        var unsafeName = await Page.APIRequest.GetAsync($"Image/qr/{qrId}?download=1&name={Uri.EscapeDataString("../evil.exe")}");
        unsafeName.Headers["content-disposition"].Should().Contain("qr-code.png").And.NotContain("evil");
    }

    [Fact]
    public async Task List_CopyLink_CopiesTheGuestUrl()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await SeedMenuAsync(ownerId, "Copy Menü");
        var qrId = await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 9", MenuId = menu });
        await Context.GrantPermissionsAsync(new[] { "clipboard-read", "clipboard-write" }, new() { Origin = App.ServerAddress });

        await GotoInteractiveAsync("/qrcodes");
        await Assertions.Expect(Page.Locator("html[data-qr-copy='ready']")).ToHaveCountAsync(1);
        var copy = Card("Tisch 9").GetByTestId("qr-copy");
        ILocator State(string state) => copy.Locator($"[data-copy-state='{state}']");
        await Assertions.Expect(State("idle")).ToBeVisibleAsync();
        await Assertions.Expect(State("idle")).ToHaveTextAsync("Copy link");
        await Assertions.Expect(State("done")).ToBeHiddenAsync();

        await copy.ClickAsync();
        await Assertions.Expect(State("done")).ToBeVisibleAsync();
        await Assertions.Expect(State("done")).ToHaveTextAsync("Copied");
        await Assertions.Expect(State("idle")).ToBeHiddenAsync();
        (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()"))
            .Should().Be($"{App.ServerAddress.TrimEnd('/')}/qr/{qrId}");

        await Assertions.Expect(State("idle")).ToBeVisibleAsync(new() { Timeout = 5_000 }); // resets after a moment
        await Assertions.Expect(State("done")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task List_DeletingScheduleQr_RemovesIt()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await SeedMenuAsync(ownerId, "Abendkarte");
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Zeitplan weg", IsTimeTableBased = true,
            TimeTableEntries = new List<TimeTableEntry>
            {
                new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(22, 0), MenuId = menu }
            }
        });

        await GotoInteractiveAsync("/qrcodes");
        await Card("Zeitplan weg").GetByRole(AriaRole.Button, new() { Name = "Delete Zeitplan weg" }).ClickAsync();
        await Assertions.Expect(Page.Locator("#confirmationModal")).ToContainTextAsync("“Zeitplan weg”");
        await Page.Locator("#confirmBtn").ClickAsync();

        await Assertions.Expect(Card("Zeitplan weg")).ToHaveCountAsync(0);
        await using var db = App.CreateDbContext(ownerId);
        (await db.QRCodes.AnyAsync(q => q.Id == qrId)).Should().BeFalse();
        (await db.TimeTableEntries.AnyAsync(e => e.QRCodeId == qrId)).Should().BeFalse();
    }

    // --- Editor preview ---

    [Fact]
    public async Task EditorPreview_OffersDownloadAndGuestUrl_AndNewCodeKeepsThePreviewedId()
    {
        var ownerId = await RegisterOwnerAsync();
        await SeedMenuAsync(ownerId, "Preview Menü");

        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Tisch 5");
        await Page.Locator("#qrTitle").PressAsync("Tab"); // InputText commits on change
        await Assertions.Expect(Page.GetByText("The link works once you have created the QR code.")).ToBeVisibleAsync();
        var previewUrl = await Page.GetByTestId("qr-url").InnerTextAsync();
        var previewId = Guid.Parse(previewUrl[(previewUrl.LastIndexOf("/qr/", StringComparison.Ordinal) + 4)..]);
        await Assertions.Expect(Page.GetByTestId("qr-download")).ToHaveAttributeAsync("download", "qr-tisch-5.png");

        await Page.Locator("#menuSelect").SelectOptionAsync(new SelectOptionValue { Label = "Preview Menü" });
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));

        // The PNG shown (and possibly downloaded) before saving belongs to the saved code
        await Assertions.Expect(Card("Tisch 5").Locator("a[href^='qr/']")).ToHaveAttributeAsync("href", $"qr/{previewId}");
        (await LoadQrAsync(ownerId, previewId)).Title.Should().Be("Tisch 5");

        await GotoInteractiveAsync($"/qrcode/edit/{previewId}");
        await Assertions.Expect(Page.GetByTestId("qr-url")).ToHaveTextAsync($"{App.ServerAddress.TrimEnd('/')}/qr/{previewId}");
        await Assertions.Expect(Page.GetByTestId("qr-download")).ToHaveAttributeAsync("href", $"Image/qr/{previewId}?download=1&name=qr-tisch-5.png");
        await Assertions.Expect(Page.GetByText("The link works once you have created the QR code.")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ScheduleEditor_StillLoadsAndSavesTimeWindows()
    {
        var ownerId = await RegisterOwnerAsync();
        var lunch = await SeedMenuAsync(ownerId, "Mittagskarte");
        var dinner = await SeedMenuAsync(ownerId, "Abendkarte");
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Zeitplan", IsTimeTableBased = true,
            TimeTableEntries = new List<TimeTableEntry>
            {
                new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(14, 0), MenuId = lunch }
            }
        });

        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");
        await Assertions.Expect(Page.Locator("#timetable")).ToBeCheckedAsync();
        await Assertions.Expect(Page.Locator("#tt-0-menu")).ToHaveValueAsync(lunch.ToString());
        await Button("Add Time Entry").ClickAsync();
        await Page.Locator("#tt-1-menu").SelectOptionAsync(new SelectOptionValue { Label = "Abendkarte" });
        await Page.Locator("#tt-1-start").FillAsync("17:00");
        await Page.Locator("#tt-1-end").FillAsync("01:00");
        await Assertions.Expect(Page.GetByTestId("timetable-entry").Nth(1)).ToContainTextAsync("17:00 – 01:00 (past midnight)");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));

        var saved = await LoadQrAsync(ownerId, qrId);
        saved.IsTimeTableBased.Should().BeTrue();
        saved.IsCalendarBased.Should().BeFalse();
        saved.TimeTableEntries.Should().HaveCount(2);
        saved.TimeTableEntries.Should().ContainSingle(e => e.MenuId == dinner && e.StartTime == new TimeOnly(17, 0) && e.EndTime == new TimeOnly(1, 0));
    }
}
