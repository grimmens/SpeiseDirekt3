using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The month calendar of a calendar-based QR code in <c>/qrcode/edit/{id}</c> (FullCalendar behind the Blazor wrapper
/// <c>Shared/Calendar/FullCalendar</c>): entries on their days with the effective menu per day, the fallback hint, creating
/// by click and drag, the entry panel, moving by drag, deleting, live conflict marks, Cancel, the read-only view and the
/// phone layout. Every test registers its own owner and seeds its menus directly.
/// </summary>
public class QrCalendarEditorTests : QrCalendarTestBase
{
    public QrCalendarEditorTests(E2EFixture fixture) : base(fixture) { }

    /// <summary>The owner works "today" at noon (cookie auth uses the same clock, so never far ahead).</summary>
    private DateOnly FixToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        App.Clock.SetLocalNow(today.ToDateTime(new TimeOnly(12, 0)));
        return today;
    }

    private sealed record Seeded(Guid OwnerId, Guid QrId, Guid Fallback, Guid Christmas, Guid Fish, Guid NewYear,
        CalendarEntry FishFridays, CalendarEntry ChristmasDays, CalendarEntry NewYearsEve);

    /// <summary>
    /// Fallback "Tageskarte"; "Fischfreitag" every Friday from Fri, Nov 6, 2026 until Fri, Jan 29, 2027; "Weihnachten"
    /// Thu, Dec 24 – Sat, Dec 26, 2026 (overrides Friday the 25th); "Silvester" only on Thu, Dec 31, 2026.
    /// </summary>
    private async Task<Seeded> SeedHolidayCalendarAsync()
    {
        var ownerId = await RegisterOwnerAsync();
        var fallback = await SeedMenuAsync(ownerId, "Tageskarte");
        var christmas = await SeedMenuAsync(ownerId, "Weihnachten");
        var fish = await SeedMenuAsync(ownerId, "Fischfreitag");
        var newYear = await SeedMenuAsync(ownerId, "Silvester");
        var fishFridays = Calendar(D(2026, 11, 6), fish, until: D(2027, 1, 29), weekly: DayOfWeek.Friday);
        var christmasDays = Calendar(D(2026, 12, 24), christmas, until: D(2026, 12, 26));
        var newYearsEve = Calendar(D(2026, 12, 31), newYear);
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Festtage", IsCalendarBased = true, MenuId = fallback,
            CalendarEntries = new List<CalendarEntry> { fishFridays, christmasDays, newYearsEve }
        });
        return new Seeded(ownerId, qrId, fallback, christmas, fish, newYear, fishFridays, christmasDays, newYearsEve);
    }

    private async Task OpenEditorAsync(Guid qrId, int year = 2026, int month = 12)
    {
        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");
        await WaitForCalendarAsync(Page);
        await GotoMonthAsync(year, month);
    }

    private ILocator Alert => Page.GetByTestId("calendar-conflicts");

    // --- Display ---

    [Fact]
    public async Task Calendar_ShowsEntriesOnTheirDays_WithTheEffectiveMenuPerDay_AndTheFallbackHint()
    {
        var today = FixToday();
        var s = await SeedHolidayCalendarAsync();
        await GotoInteractiveAsync($"/qrcode/edit/{s.QrId}");
        await WaitForCalendarAsync(Page);

        // Today's month first, today marked
        await Assertions.Expect(CalendarOf(Page).Locator($"td.fc-day-today[data-date='{Iso(today)}']")).ToHaveCountAsync(1);

        await GotoMonthAsync(2026, 12);
        // Month grid: a single date, the first day of a period, weekly occurrences; the period wins on Friday the 25th
        await Assertions.Expect(EventsIn(D(2026, 12, 31), "Silvester")).ToHaveCountAsync(1);
        await Assertions.Expect(EventsIn(D(2026, 12, 24), "Weihnachten")).ToHaveCountAsync(1);
        foreach (var friday in new[] { 4, 11, 18 })
        {
            await Assertions.Expect(EventsIn(D(2026, 12, friday), "Fischfreitag")).ToHaveCountAsync(1);
            await Assertions.Expect(EventsIn(D(2026, 12, friday), "Fischfreitag")).Not.ToHaveClassAsync(new Regex("sd-cal-overridden"));
        }
        var overridden = EventsIn(D(2026, 12, 25), "Fischfreitag");
        await Assertions.Expect(overridden).ToHaveClassAsync(new Regex("sd-cal-overridden"));
        await Assertions.Expect(overridden).ToHaveAttributeAsync("title", "Fischfreitag: not on Fri, Dec 25, 2026, Weihnachten applies that day");
        await Assertions.Expect(EventsIn(D(2026, 12, 27))).ToHaveCountAsync(0);

        // The list view names every day: the period on each of its days, the weekly entry on each Friday
        var december = await ReadListViewAsync();
        TitlesOn(december, D(2026, 12, 24)).Should().Equal("Weihnachten");
        TitlesOn(december, D(2026, 12, 25)).Should().BeEquivalentTo("Weihnachten", "Fischfreitag");
        december.Single(r => r.Date == "2026-12-25" && r.Title == "Fischfreitag").Overridden.Should().BeTrue();
        TitlesOn(december, D(2026, 12, 26)).Should().Equal("Weihnachten");
        TitlesOn(december, D(2026, 12, 27)).Should().BeEmpty();
        TitlesOn(december, D(2026, 12, 31)).Should().Equal("Silvester");
        december.Where(r => r.Title == "Fischfreitag").Select(r => r.Date).Should().Equal("2026-12-04", "2026-12-11", "2026-12-18", "2026-12-25");
        december.Should().NotContain(r => r.Conflict);

        // The weekly entry starts on Nov 6 and ends with its until date (Jan 29)
        await GotoMonthAsync(2026, 11);
        (await ReadListViewAsync()).Where(r => r.Title == "Fischfreitag").Select(r => r.Date)
            .Should().Equal("2026-11-06", "2026-11-13", "2026-11-20", "2026-11-27");
        await GotoMonthAsync(2027, 1);
        (await ReadListViewAsync()).Where(r => r.Title == "Fischfreitag").Select(r => r.Date)
            .Should().Equal("2027-01-01", "2027-01-08", "2027-01-15", "2027-01-22", "2027-01-29");
        await GotoMonthAsync(2027, 2);
        (await ReadListViewAsync()).Should().BeEmpty();

        // Legend: one stable colour per menu (the same in the calendar and the legend), the fallback and the struck style
        var legend = Page.GetByTestId("calendar-legend");
        await Assertions.Expect(legend.GetByTestId("legend-menu")).ToHaveTextAsync(new[] { "Fischfreitag", "Silvester", "Weihnachten" });
        await Assertions.Expect(legend.GetByTestId("calendar-fallback-hint")).ToHaveTextAsync("Other days: Tageskarte (fallback menu)");
        await GotoMonthAsync(2026, 12);
        await Assertions.Expect(legend.GetByTestId("legend-overridden")).ToBeVisibleAsync();
        foreach (var (date, menu) in new[] { (D(2026, 12, 31), "Silvester"), (D(2026, 12, 24), "Weihnachten"), (D(2026, 12, 18), "Fischfreitag") })
        {
            var eventColor = await EventsIn(date, menu).EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
            var swatchColor = await legend.GetByTestId("legend-menu").Filter(new() { HasText = menu }).Locator(".sd-cal-swatch")
                .EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
            eventColor.Should().Be(swatchColor, $"{menu} has one colour");
        }
        var colors = await legend.GetByTestId("legend-menu").Locator(".sd-cal-swatch")
            .EvaluateAllAsync<string[]>("els => els.map(e => getComputedStyle(e).backgroundColor)");
        colors.Should().OnlyHaveUniqueItems();

        // The hint follows the fallback select
        await Page.Locator("#fallbackMenuSelect").SelectOptionAsync(new SelectOptionValue { Label = "-- No menu --" });
        await Assertions.Expect(legend.GetByTestId("calendar-fallback-hint")).ToHaveTextAsync("Other days: no menu (guests see a notice)");

        // The keyboard list: one row per entry, in date order
        await Assertions.Expect(Page.GetByTestId("calendar-entry-summary")).ToHaveTextAsync(new[]
        {
            "Every Friday from Fri, Nov 6, 2026 until Fri, Jan 29, 2027",
            "Every day from Thu, Dec 24, 2026 to Sat, Dec 26, 2026",
            "Only on Thu, Dec 31, 2026"
        });
        await Assertions.Expect(Alert).ToHaveCountAsync(0);
    }

    // --- Creating ---

    [Fact]
    public async Task ClickingADay_OpensThePanelForThatDate_AndAddsASingleDateEntry()
    {
        FixToday();
        var ownerId = await RegisterOwnerAsync();
        var fallback = await SeedMenuAsync(ownerId, "Tageskarte");
        var christmas = await SeedMenuAsync(ownerId, "Weihnachten");

        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Heiligabend");
        await SelectCalendarModeAsync();
        await GotoMonthAsync(2026, 12);

        await ClickDayAsync(D(2026, 12, 24));
        await ExpectPanelOpenAsync();
        await Assertions.Expect(Panel.GetByRole(AriaRole.Heading, new() { Name = "New calendar entry" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#cal-panel-date")).ToHaveValueAsync("2026-12-24");
        await Assertions.Expect(Page.Locator("#cal-panel-until")).ToHaveValueAsync("");
        await Assertions.Expect(Page.Locator("#cal-panel-weekly")).ToHaveValueAsync("");
        await Assertions.Expect(Page.Locator("#cal-panel-menu")).ToBeFocusedAsync();
        await Assertions.Expect(Page.GetByTestId("calendar-panel-summary")).ToHaveTextAsync("Only on Thu, Dec 24, 2026");

        // A menu is required
        await Page.GetByTestId("calendar-panel-apply").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("calendar-panel-error")).ToHaveTextAsync("Choose the menu guests get.");
        await ExpectPanelOpenAsync();

        await SelectPanelMenuAsync("Weihnachten");
        await ApplyPanelAsync();
        await Assertions.Expect(EntryRows).ToHaveCountAsync(1);
        await Assertions.Expect(EntryRow("Only on Thu, Dec 24, 2026").GetByTestId("calendar-entry-menu")).ToHaveTextAsync("Weihnachten");
        await Assertions.Expect(EventsIn(D(2026, 12, 24), "Weihnachten")).ToHaveCountAsync(1);

        await Page.Locator("#fallbackMenuSelect").SelectOptionAsync(new SelectOptionValue { Label = "Tageskarte" });
        await Button("Create QR Code").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));

        await using var db = App.CreateDbContext(ownerId);
        var saved = await db.QRCodes.AsNoTracking().Include(q => q.CalendarEntries).SingleAsync(q => q.Title == "Heiligabend");
        saved.IsCalendarBased.Should().BeTrue();
        saved.MenuId.Should().Be(fallback);
        saved.CalendarEntries.Should().ContainSingle().Which.Should().Match<CalendarEntry>(e =>
            e.Date == D(2026, 12, 24) && e.EndDate == null && e.RecurringDayOfWeek == null && e.MenuId == christmas);
    }

    [Fact]
    public async Task DraggingAcrossDays_CreatesAPeriod()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId);

        await DragAcrossDaysAsync(D(2026, 12, 28), D(2026, 12, 30));
        await ExpectPanelOpenAsync();
        await Assertions.Expect(Page.Locator("#cal-panel-date")).ToHaveValueAsync("2026-12-28");
        await Assertions.Expect(Page.Locator("#cal-panel-until")).ToHaveValueAsync("2026-12-30");
        await Assertions.Expect(Page.GetByTestId("calendar-panel-summary")).ToHaveTextAsync("Every day from Mon, Dec 28, 2026 to Wed, Dec 30, 2026");
        await SelectPanelMenuAsync("Weihnachten");
        await ApplyPanelAsync();

        var rows = await ReadListViewAsync();
        foreach (var day in new[] { 28, 29, 30 })
            TitlesOn(rows, D(2026, 12, day)).Should().Equal("Weihnachten");

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        (await LoadQrAsync(s.OwnerId, s.QrId)).CalendarEntries.Should().HaveCount(4).And.ContainSingle(e =>
            e.Date == D(2026, 12, 28) && e.EndDate == D(2026, 12, 30) && e.RecurringDayOfWeek == null && e.MenuId == s.Christmas);
    }

    [Fact]
    public async Task AddEntryButton_OpensThePanel_ForTheMonthOnScreen()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId, 2027, 3);

        await Page.GetByTestId("calendar-add-entry").ClickAsync();
        await ExpectPanelOpenAsync();
        await Assertions.Expect(Page.Locator("#cal-panel-date")).ToHaveValueAsync("2027-03-01");

        // Keyboard only: menu, weekly Monday, Enter applies (it must not submit the editor's form)
        await SelectPanelMenuAsync("Tageskarte");
        await Page.Locator("#cal-panel-weekly").SelectOptionAsync("Monday");
        await Page.Locator("#cal-panel-until").FillAsync("2027-03-29");
        await Page.Locator("#cal-panel-until").PressAsync("Enter");
        await ExpectPanelClosedAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/qrcode/edit/{s.QrId}$"));
        await Assertions.Expect(EntryRow("Every Monday from Mon, Mar 1, 2027 until Mon, Mar 29, 2027")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByTestId("calendar-add-entry")).ToBeFocusedAsync();
    }

    // --- Editing ---

    [Fact]
    public async Task ClickingAnEntry_OpensThePanel_AndTheChangesAreSaved()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId);

        await EventsIn(D(2026, 12, 31), "Silvester").ClickAsync();
        await ExpectPanelOpenAsync();
        await Assertions.Expect(Panel.GetByRole(AriaRole.Heading, new() { Name = "Edit calendar entry" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#cal-panel-menu")).ToHaveValueAsync(s.NewYear.ToString());
        await Assertions.Expect(Page.Locator("#cal-panel-date")).ToHaveValueAsync("2026-12-31");

        await SelectPanelMenuAsync("Fischfreitag");
        await Page.Locator("#cal-panel-weekly").SelectOptionAsync("Thursday");
        await Page.Locator("#cal-panel-until").FillAsync("2027-01-28");
        await Assertions.Expect(Page.GetByTestId("calendar-panel-summary"))
            .ToHaveTextAsync("Every Thursday from Thu, Dec 31, 2026 until Thu, Jan 28, 2027");
        await ApplyPanelAsync();

        await Assertions.Expect(EntryRow("Every Thursday from Thu, Dec 31, 2026 until Thu, Jan 28, 2027").GetByTestId("calendar-entry-menu"))
            .ToHaveTextAsync("Fischfreitag");
        await GotoMonthAsync(2027, 1);
        (await ReadListViewAsync()).Where(r => r.Title == "Fischfreitag" && DateOnly.Parse(r.Date).DayOfWeek == DayOfWeek.Thursday)
            .Select(r => r.Date).Should().Equal("2027-01-07", "2027-01-14", "2027-01-21", "2027-01-28");

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        var saved = await LoadQrAsync(s.OwnerId, s.QrId);
        saved.CalendarEntries.Should().HaveCount(3).And.ContainSingle(e => e.Id == s.NewYearsEve.Id && e.MenuId == s.Fish
            && e.RecurringDayOfWeek == DayOfWeek.Thursday && e.Date == D(2026, 12, 31) && e.EndDate == D(2027, 1, 28));
    }

    [Fact]
    public async Task DraggingAnEntry_MovesItsDates_AndAWeeklyEntryMovesAsAWhole()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId);

        // A single date one day earlier
        await DragEventToDayAsync(EventsIn(D(2026, 12, 31), "Silvester"), D(2026, 12, 30));
        await Assertions.Expect(EntryRow("Only on Wed, Dec 30, 2026")).ToHaveCountAsync(1);
        await Assertions.Expect(EventsIn(D(2026, 12, 30), "Silvester")).ToHaveCountAsync(1);
        await Assertions.Expect(EventsIn(D(2026, 12, 31), "Silvester")).ToHaveCountAsync(0);

        // A period a week later: both ends move
        await DragEventToDayAsync(EventsIn(D(2026, 12, 24), "Weihnachten"), D(2026, 12, 17));
        await Assertions.Expect(EntryRow("Every day from Thu, Dec 17, 2026 to Sat, Dec 19, 2026")).ToHaveCountAsync(1);

        // Friday → Thursday: the weekly entry's weekday, first day and until date move one day
        await DragEventToDayAsync(EventsIn(D(2026, 12, 11), "Fischfreitag"), D(2026, 12, 10));
        await Assertions.Expect(EntryRow("Every Thursday from Thu, Nov 5, 2026 until Thu, Jan 28, 2027")).ToHaveCountAsync(1);
        await Assertions.Expect(EventsIn(D(2026, 12, 3), "Fischfreitag")).ToHaveCountAsync(1);
        await Assertions.Expect(EventsIn(D(2026, 12, 4), "Fischfreitag")).ToHaveCountAsync(0);

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        var saved = (await LoadQrAsync(s.OwnerId, s.QrId)).CalendarEntries!;
        saved.Should().ContainSingle(e => e.Id == s.NewYearsEve.Id && e.Date == D(2026, 12, 30) && e.EndDate == null);
        saved.Should().ContainSingle(e => e.Id == s.ChristmasDays.Id && e.Date == D(2026, 12, 17) && e.EndDate == D(2026, 12, 19));
        saved.Should().ContainSingle(e => e.Id == s.FishFridays.Id && e.RecurringDayOfWeek == DayOfWeek.Thursday
            && e.Date == D(2026, 11, 5) && e.EndDate == D(2027, 1, 28));
    }

    [Fact]
    public async Task StretchingAPeriod_ChangesItsLastDay()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId);

        await CenterCalendarAsync(Page);
        var bar = EventsIn(D(2026, 12, 24), "Weihnachten");
        await bar.HoverAsync();
        var handle = bar.Locator(".fc-event-resizer-end");
        var box = await handle.BoundingBoxAsync() ?? throw new InvalidOperationException("No resize handle");
        var target = await DayCell(D(2026, 12, 27)).BoundingBoxAsync() ?? throw new InvalidOperationException("No cell");
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(target.X + target.Width / 2, box.Y + box.Height / 2, new() { Steps = 12 });
        await Page.Mouse.UpAsync();

        await Assertions.Expect(EntryRow("Every day from Thu, Dec 24, 2026 to Sun, Dec 27, 2026")).ToHaveCountAsync(1);
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        (await LoadQrAsync(s.OwnerId, s.QrId)).CalendarEntries.Should()
            .ContainSingle(e => e.Id == s.ChristmasDays.Id && e.Date == D(2026, 12, 24) && e.EndDate == D(2026, 12, 27));
    }

    [Fact]
    public async Task Deleting_FromThePanelAndFromTheList_RemovesTheEntriesOnSave()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId);

        await EventsIn(D(2026, 12, 31), "Silvester").ClickAsync();
        await ExpectPanelOpenAsync();
        await Page.GetByTestId("calendar-panel-delete").ClickAsync();
        await ExpectPanelClosedAsync();
        await Assertions.Expect(EventsIn(D(2026, 12, 31))).ToHaveCountAsync(0);

        await EntryRow("Every day from Thu, Dec 24, 2026 to Sat, Dec 26, 2026").GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Assertions.Expect(EntryRows).ToHaveCountAsync(1);
        // Without the period, Friday the 25th is a fish day again
        await Assertions.Expect(EventsIn(D(2026, 12, 25), "Fischfreitag")).Not.ToHaveClassAsync(new Regex("sd-cal-overridden"));
        await Assertions.Expect(Page.GetByTestId("legend-overridden")).ToHaveCountAsync(0);

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        (await LoadQrAsync(s.OwnerId, s.QrId)).CalendarEntries.Should().ContainSingle().Which.Id.Should().Be(s.FishFridays.Id);
        await using var db = App.CreateDbContext(s.OwnerId);
        (await db.CalendarEntries.AnyAsync(e => e.Id == s.NewYearsEve.Id || e.Id == s.ChristmasDays.Id)).Should().BeFalse();
    }

    // --- Conflicts ---

    [Fact]
    public async Task ConflictingEntry_IsMarkedAtOnce_AndSaveIsRefusedUntilItIsResolved()
    {
        var s = await SeedHolidayCalendarAsync();
        await OpenEditorAsync(s.QrId);

        // A period that shares Saturday the 26th with Christmas: the panel warns before it is applied
        await DragAcrossDaysAsync(D(2026, 12, 26), D(2026, 12, 27));
        await ExpectPanelOpenAsync();
        await SelectPanelMenuAsync("Silvester");
        await Assertions.Expect(Page.GetByTestId("calendar-panel-conflict"))
            .ToContainTextAsync("Overlaps “Weihnachten: Every day from Thu, Dec 24, 2026 to Sat, Dec 26, 2026”");
        await ApplyPanelAsync();

        // Marked in the calendar, the list and the alert — before "Save"
        await Assertions.Expect(EventsIn(D(2026, 12, 24), "Weihnachten")).ToHaveClassAsync(new Regex("sd-cal-conflict"));
        await Assertions.Expect(EventsIn(D(2026, 12, 26), "Silvester")).ToHaveClassAsync(new Regex("sd-cal-conflict"));
        await Assertions.Expect(EventsIn(D(2026, 12, 31), "Silvester")).Not.ToHaveClassAsync(new Regex("sd-cal-conflict"));
        await Assertions.Expect(Alert).ToContainTextAsync("“Weihnachten: Every day from Thu, Dec 24, 2026 to Sat, Dec 26, 2026” and “Silvester: Every day from Sat, Dec 26, 2026 to Sun, Dec 27, 2026” overlap.");
        await Assertions.Expect(Page.GetByTestId("calendar-entry-conflict")).ToHaveCountAsync(2);

        // "Save" is refused with the model's message, nothing is written
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page.GetByText("Die Datumsperioden überschneiden sich").First).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/qrcode/edit/{s.QrId}$"));
        (await LoadQrAsync(s.OwnerId, s.QrId)).CalendarEntries.Should().HaveCount(3);

        // Moving the new period one day later resolves it: marks, alert and the old message disappear
        await DragEventToDayAsync(EventsIn(D(2026, 12, 26), "Silvester"), D(2026, 12, 27));
        await Assertions.Expect(EntryRow("Every day from Sun, Dec 27, 2026 to Mon, Dec 28, 2026")).ToHaveCountAsync(1);
        await Assertions.Expect(Alert).ToHaveCountAsync(0);
        await Assertions.Expect(CalendarOf(Page).Locator(".sd-cal-conflict")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByText("Die Datumsperioden überschneiden sich")).ToHaveCountAsync(0);

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        (await LoadQrAsync(s.OwnerId, s.QrId)).CalendarEntries.Should().HaveCount(4);
    }

    // --- Cancel ---

    [Fact]
    public async Task PanelCancelAndEscape_DiscardThePanel_AndPageCancel_LeavesTheDatabaseUntouched()
    {
        var s = await SeedHolidayCalendarAsync();
        var before = await LoadQrAsync(s.OwnerId, s.QrId);
        await OpenEditorAsync(s.QrId);

        // Panel: Escape and Cancel discard what was changed in it
        await EventsIn(D(2026, 12, 31), "Silvester").ClickAsync();
        await ExpectPanelOpenAsync();
        await SelectPanelMenuAsync("Tageskarte");
        await Page.Keyboard.PressAsync("Escape");
        await ExpectPanelClosedAsync();
        await Assertions.Expect(EntryRow("Only on Thu, Dec 31, 2026").GetByTestId("calendar-entry-menu")).ToHaveTextAsync("Silvester");

        await EntryRow("Only on Thu, Dec 31, 2026").GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await ExpectPanelOpenAsync();
        await Page.Locator("#cal-panel-date").FillAsync("2026-12-30");
        await Panel.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await ExpectPanelClosedAsync();
        await Assertions.Expect(EntryRow("Only on Thu, Dec 31, 2026")).ToHaveCountAsync(1);
        // Focus returns to the list's Edit button the panel was opened from
        await Assertions.Expect(EntryRow("Only on Thu, Dec 31, 2026").GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToBeFocusedAsync();

        // Changes in the calendar are a draft: move, remove, add — then the page's Cancel
        await DragEventToDayAsync(EventsIn(D(2026, 12, 31), "Silvester"), D(2026, 12, 29));
        await Assertions.Expect(EntryRow("Only on Tue, Dec 29, 2026")).ToHaveCountAsync(1);
        await EntryRow("Every day from Thu, Dec 24, 2026 to Sat, Dec 26, 2026").GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await ClickDayAsync(D(2026, 12, 8));
        await ExpectPanelOpenAsync();
        await SelectPanelMenuAsync("Weihnachten");
        await ApplyPanelAsync();
        await Assertions.Expect(EntryRows).ToHaveCountAsync(3);

        await Button("Cancel", exact: true).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        var after = await LoadQrAsync(s.OwnerId, s.QrId);
        after.CalendarEntries.Should().BeEquivalentTo(before.CalendarEntries, o => o.Excluding(e => e.QRCode).Excluding(e => e.Menu));
    }

    // --- Read-only ---

    [Fact]
    public async Task UserWithoutEditPermission_SeesTheCalendar_ButCannotChangeAnything()
    {
        var s = await SeedHolidayCalendarAsync();
        var before = await LoadQrAsync(s.OwnerId, s.QrId);
        var cashierEmail = UniqueEmail("cashier");
        await InviteAsync(cashierEmail, "Carla Kassa", "Cashier"); // QrCodesView, no QrCodesEdit
        var (cashierContext, cashier) = await NewContextAsync();
        await using var _ = cashierContext;
        await LoginAsync(cashier, cashierEmail);

        await GotoInteractiveAsync(cashier, "/qrcodes");
        var card = cashier.GetByTestId("qr-card").Filter(new() { HasText = "Festtage" });
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToHaveCountAsync(0);
        await card.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        await Assertions.Expect(cashier).ToHaveURLAsync(new Regex($"/qrcode/edit/{s.QrId}$"));
        await WaitForCalendarAsync(cashier);

        await Assertions.Expect(cashier.GetByTestId("qr-readonly-notice")).ToBeVisibleAsync();
        await Assertions.Expect(cashier.Locator("#qrTitle")).ToBeDisabledAsync();
        await Assertions.Expect(cashier.Locator("#fallbackMenuSelect")).ToBeDisabledAsync();
        await Assertions.Expect(cashier.GetByRole(AriaRole.Button, new() { Name = "Save Changes" })).ToHaveCountAsync(0);
        await Assertions.Expect(cashier.GetByTestId("calendar-add-entry")).ToHaveCountAsync(0);
        await Assertions.Expect(cashier.GetByTestId("calendar-entry").GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        await Assertions.Expect(cashier.GetByTestId("calendar-entry")).ToHaveCountAsync(3);

        // The month buttons work, the entries are shown — but clicks and drags change nothing
        await GotoMonthAsync(cashier, 2026, 12);
        var silvester = CalendarOf(cashier).Locator("td[data-date='2026-12-31'] .fc-event");
        await Assertions.Expect(silvester).ToHaveCountAsync(1);
        await ClickDayAsync(cashier, D(2026, 12, 8));
        await silvester.ClickAsync();
        await CenterCalendarAsync(cashier);
        var box = (await silvester.BoundingBoxAsync())!;
        var target = (await CalendarOf(cashier).Locator("td[data-date='2026-12-29']").BoundingBoxAsync())!;
        await cashier.Mouse.MoveAsync(box.X + 10, box.Y + box.Height / 2);
        await cashier.Mouse.DownAsync();
        await cashier.Mouse.MoveAsync(target.X + target.Width / 2, target.Y + target.Height - 6, new() { Steps = 12 });
        await cashier.Mouse.UpAsync();
        await cashier.WaitForTimeoutAsync(500);
        await Assertions.Expect(cashier.Locator("dialog[data-testid='calendar-panel'][open]")).ToHaveCountAsync(0);
        await Assertions.Expect(silvester).ToHaveCountAsync(1);
        await Assertions.Expect(cashier.GetByTestId("calendar-entry-summary").GetByText("Only on Thu, Dec 31, 2026", new() { Exact = true })).ToHaveCountAsync(1);

        await cashier.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
        await Assertions.Expect(cashier).ToHaveURLAsync(new Regex("/qrcodes$"));
        var after = await LoadQrAsync(s.OwnerId, s.QrId);
        after.CalendarEntries.Should().BeEquivalentTo(before.CalendarEntries, o => o.Excluding(e => e.QRCode).Excluding(e => e.Menu));
        after.Title.Should().Be("Festtage");

        // Creating stays denied without QrCodesCreate
        await GotoInteractiveAsync(cashier, "/qrcode/edit");
        await Assertions.Expect(cashier).ToHaveURLAsync(new Regex("/qrcodes$"));
    }

    // --- Phone ---

    [Fact]
    public async Task Phone390_FitsWithoutHorizontalScrolling_AndThePanelIsABottomSheet()
    {
        var s = await SeedHolidayCalendarAsync();
        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();

        await GotoInteractiveAsync(phone, $"/qrcode/edit/{s.QrId}");
        await WaitForCalendarAsync(phone);
        await GotoMonthAsync(phone, 2026, 12);

        async Task ExpectNoHorizontalOverflowAsync(string what)
        {
            var page = await phone.EvaluateAsync<int[]>(
                "() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
            page[0].Should().BeLessThanOrEqualTo(page[1], $"{what} fits the 390 px screen");
            var calendar = await CalendarOf(phone).EvaluateAsync<int[]>("e => [e.scrollWidth, e.clientWidth]");
            calendar[0].Should().BeLessThanOrEqualTo(calendar[1] + 1, $"{what}: the calendar has no inner horizontal scrolling");
        }

        await ExpectNoHorizontalOverflowAsync("the month grid");
        // All seven weekday columns are on screen and the events readable
        var lastHeader = await CalendarOf(phone).Locator(".fc-col-header-cell").Last.BoundingBoxAsync();
        (lastHeader!.X + lastHeader.Width).Should().BeLessThanOrEqualTo(390);
        await Assertions.Expect(CalendarOf(phone).Locator("td[data-date='2026-12-31'] .fc-event")).ToBeVisibleAsync();

        // A tap on a day opens the panel as a bottom sheet over the full width
        await ClickDayAsync(phone, D(2026, 12, 8));
        var panel = phone.Locator("dialog[data-testid='calendar-panel'][open]");
        await Assertions.Expect(panel).ToHaveCountAsync(1);
        var sheet = (await panel.BoundingBoxAsync())!;
        sheet.Width.Should().BeApproximately(390, 1);
        (sheet.Y + sheet.Height).Should().BeApproximately(844, 1);
        await Assertions.Expect(phone.Locator("#cal-panel-date")).ToHaveValueAsync("2026-12-08");
        await phone.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToHaveCountAsync(0);

        // List view on the phone
        var rows = await ReadListViewAsync(phone);
        TitlesOn(rows, D(2026, 12, 31)).Should().Equal("Silvester");
        await ExpectNoHorizontalOverflowAsync("the page after the list view");
    }
}
