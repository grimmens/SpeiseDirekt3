using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Seed and UI helpers for the QR code editor and its calendar (<c>QrCodeCalendarEntries</c> on top of the
/// <c>FullCalendar</c> wrapper): menus and QR codes seeded straight into the database, month navigation, day cells,
/// mouse drags, the entry panel and a readout of the calendar's list view (date → entries).
/// </summary>
public abstract class QrCalendarTestBase : BackOfficeTestBase
{
    protected QrCalendarTestBase(E2EFixture fixture) : base(fixture) { }

    // --- Seeding ---

    /// <summary>Registers a fresh owner in <see cref="E2ETestBase.Page"/> and returns its tenant id.</summary>
    protected async Task<Guid> RegisterOwnerAsync()
    {
        var email = await RegisterNewOwnerAsync();
        return await OwnerIdAsync(email);
    }

    /// <summary>A menu with one category and one item ("{name} Gericht"), so the guest page renders it.</summary>
    protected async Task<Guid> SeedMenuAsync(Guid ownerId, string name)
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

    protected async Task<Guid> SeedQrAsync(Guid ownerId, QRCode qr)
    {
        qr.Id = qr.Id == Guid.Empty ? Guid.NewGuid() : qr.Id;
        foreach (var e in qr.TimeTableEntries ?? new()) e.QRCodeId = qr.Id;
        foreach (var e in qr.CalendarEntries ?? new()) e.QRCodeId = qr.Id;
        await App.SeedAsync(ownerId, db => { db.QRCodes.Add(qr); return Task.CompletedTask; });
        return qr.Id;
    }

    protected static CalendarEntry Calendar(DateOnly date, Guid menuId, DateOnly? until = null, DayOfWeek? weekly = null) =>
        new() { Id = Guid.NewGuid(), Date = date, EndDate = until, RecurringDayOfWeek = weekly, MenuId = menuId };

    protected async Task<QRCode> LoadQrAsync(Guid ownerId, Guid qrId)
    {
        await using var db = App.CreateDbContext(ownerId);
        return await db.QRCodes.AsNoTracking()
            .Include(q => q.CalendarEntries)
            .Include(q => q.TimeTableEntries)
            .SingleAsync(q => q.Id == qrId);
    }

    protected static DateOnly D(int year, int month, int day) => new(year, month, day);

    protected static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // --- Editor ---

    /// <summary>
    /// Picks "Calendar-based Menu". The title field commits on blur, i.e. when the radio is clicked; the re-render for
    /// the title briefly re-checks the old radio until the mode change arrives, so wait for the final state.
    /// </summary>
    protected async Task SelectCalendarModeAsync()
    {
        await Page.Locator("#calendar").ClickAsync();
        await Assertions.Expect(Page.Locator("#calendar")).ToBeCheckedAsync();
        await WaitForCalendarAsync(Page);
    }

    protected ILocator EntryRows => Page.GetByTestId("calendar-entry");

    /// <summary>The list row of the entry whose sentence is exactly <paramref name="summary"/>.</summary>
    protected ILocator EntryRow(string summary) =>
        EntryRows.Filter(new() { Has = Page.GetByTestId("calendar-entry-summary").GetByText(summary, new() { Exact = true }) });

    protected ILocator Panel => Page.GetByTestId("calendar-panel");

    protected async Task ExpectPanelOpenAsync()
    {
        await Assertions.Expect(Panel).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("dialog[data-testid='calendar-panel'][open]")).ToHaveCountAsync(1);
    }

    protected async Task ExpectPanelClosedAsync() =>
        await Assertions.Expect(Page.Locator("dialog[data-testid='calendar-panel'][open]")).ToHaveCountAsync(0);

    protected Task SelectPanelMenuAsync(string menuName) =>
        Page.Locator("#cal-panel-menu").SelectOptionAsync(new SelectOptionValue { Label = menuName });

    protected async Task ApplyPanelAsync()
    {
        await Page.GetByTestId("calendar-panel-apply").ClickAsync();
        await ExpectPanelClosedAsync();
    }

    /// <summary>"Add entry" → fills the panel (menu by visible name) → applies.</summary>
    protected async Task AddEntryViaPanelAsync(string menuName, string date, string? until = null, string? weekly = null)
    {
        await Page.GetByTestId("calendar-add-entry").ClickAsync();
        await ExpectPanelOpenAsync();
        await SelectPanelMenuAsync(menuName);
        if (weekly is not null)
            await Page.Locator("#cal-panel-weekly").SelectOptionAsync(weekly);
        await Page.Locator("#cal-panel-date").FillAsync(date);
        if (until is not null)
            await Page.Locator("#cal-panel-until").FillAsync(until);
        await ApplyPanelAsync();
    }

    // --- Calendar ---

    protected static ILocator CalendarOf(IPage page) => page.GetByTestId("qr-calendar");

    protected static Task WaitForCalendarAsync(IPage page) =>
        Assertions.Expect(CalendarOf(page).Locator("[data-calendar-ready]")).ToHaveCountAsync(1);

    /// <summary>Clicks prev/next until the calendar shows <paramref name="year"/>/<paramref name="month"/>.</summary>
    protected static async Task GotoMonthAsync(IPage page, int year, int month)
    {
        var calendar = CalendarOf(page);
        var wanted = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        for (var i = 0; i < 48; i++)
        {
            var title = (await calendar.Locator(".fc-toolbar-title").InnerTextAsync()).Trim();
            if (title == wanted)
                return;
            var shown = DateTime.ParseExact(title, "MMMM yyyy", CultureInfo.InvariantCulture);
            var button = shown < new DateTime(year, month, 1) ? ".fc-next-button" : ".fc-prev-button";
            await calendar.Locator(button).ClickAsync();
            await Assertions.Expect(calendar.Locator(".fc-toolbar-title")).Not.ToHaveTextAsync(title);
        }
        throw new InvalidOperationException($"Calendar did not reach {wanted}.");
    }

    protected Task GotoMonthAsync(int year, int month) => GotoMonthAsync(Page, year, month);

    protected static ILocator DayCell(IPage page, DateOnly date) =>
        CalendarOf(page).Locator($"td.fc-daygrid-day[data-date='{Iso(date)}']");

    protected ILocator DayCell(DateOnly date) => DayCell(Page, date);

    /// <summary>Events whose element starts in the day's cell (single days, the first day of a bar, weekly occurrences).</summary>
    protected ILocator EventsIn(DateOnly date, string? title = null) =>
        title is null
            ? DayCell(date).Locator(".fc-event")
            : DayCell(date).Locator(".fc-event", new() { Has = Page.Locator(".fc-event-title", new() { HasTextRegex = ExactText(title) }) });

    /// <summary>
    /// Scrolls the month grid to the middle of the viewport: the mouse helpers below work with viewport coordinates,
    /// which do not scroll by themselves like locator clicks. Instantly: Bootstrap's reboot makes scrolling smooth, and a
    /// box read during the animation is stale.
    /// </summary>
    protected static Task CenterCalendarAsync(IPage page) =>
        CalendarOf(page).Locator(".fc-view-harness").EvaluateAsync("e => e.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'instant' })");

    /// <summary>A point in the empty lower part of a day cell (events sit at the top), in viewport coordinates.</summary>
    private static async Task<(float X, float Y)> EmptyPointAsync(ILocator cell)
    {
        var box = await cell.BoundingBoxAsync() ?? throw new InvalidOperationException("Day cell is not visible.");
        return (box.X + box.Width / 2, box.Y + box.Height - 6);
    }

    /// <summary>Clicks the free space of a day (not an event).</summary>
    protected static async Task ClickDayAsync(IPage page, DateOnly date)
    {
        await CenterCalendarAsync(page);
        var (x, y) = await EmptyPointAsync(DayCell(page, date));
        await page.Mouse.ClickAsync(x, y);
    }

    protected Task ClickDayAsync(DateOnly date) => ClickDayAsync(Page, date);

    /// <summary>Presses on the free space of <paramref name="from"/>, drags to <paramref name="to"/> and releases.</summary>
    protected static async Task DragAcrossDaysAsync(IPage page, DateOnly from, DateOnly to)
    {
        await CenterCalendarAsync(page);
        var (x1, y1) = await EmptyPointAsync(DayCell(page, from));
        var (x2, y2) = await EmptyPointAsync(DayCell(page, to));
        await page.Mouse.MoveAsync(x1, y1);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x1 + 8, y1, new() { Steps = 3 });
        await page.Mouse.MoveAsync(x2, y2, new() { Steps = 12 });
        await page.Mouse.UpAsync();
    }

    protected Task DragAcrossDaysAsync(DateOnly from, DateOnly to) => DragAcrossDaysAsync(Page, from, to);

    /// <summary>Drags an event element onto the free space of another day.</summary>
    protected static async Task DragEventToDayAsync(IPage page, ILocator eventElement, DateOnly to)
    {
        await CenterCalendarAsync(page);
        var box = await eventElement.BoundingBoxAsync() ?? throw new InvalidOperationException("Event is not visible.");
        var (x2, y2) = await EmptyPointAsync(DayCell(page, to));
        var (x1, y1) = (box.X + Math.Min(12, box.Width / 2), box.Y + box.Height / 2);
        await page.Mouse.MoveAsync(x1, y1);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x1 + 10, y1 + 4, new() { Steps = 3 });
        await page.Mouse.MoveAsync(x2, y2, new() { Steps = 15 });
        await page.Mouse.UpAsync();
    }

    protected Task DragEventToDayAsync(ILocator eventElement, DateOnly to) => DragEventToDayAsync(Page, eventElement, to);

    /// <summary>One row of the calendar's list view: the day, the entry title and its CSS classes.</summary>
    protected sealed class ListedEntry
    {
        public string Date { get; set; } = "";
        public string Title { get; set; } = "";
        public string Classes { get; set; } = "";
        public bool Overridden => Classes.Split(' ').Contains("sd-cal-overridden");
        public bool Conflict => Classes.Split(' ').Contains("sd-cal-conflict");
    }

    /// <summary>
    /// Switches to the list view of the month on screen, reads every day with its entries (a period is listed on each of
    /// its days, a weekly entry on each occurrence) and switches back to the month grid.
    /// </summary>
    protected static async Task<IReadOnlyList<ListedEntry>> ReadListViewAsync(IPage page)
    {
        var calendar = CalendarOf(page);
        await calendar.Locator(".fc-listMonth-button").ClickAsync();
        await Assertions.Expect(calendar.Locator(".fc-list, .fc-list-empty").First).ToBeVisibleAsync();
        var rows = await calendar.EvaluateAsync<ListedEntry[]>("""
            root => {
                const result = [];
                let date = null;
                for (const row of root.querySelectorAll('.fc-list-table tr')) {
                    if (row.classList.contains('fc-list-day')) date = row.dataset.date;
                    else if (row.classList.contains('fc-list-event'))
                        result.push({ Date: date, Title: row.querySelector('.fc-list-event-title').textContent.trim(), Classes: row.className });
                }
                return result;
            }
            """);
        await calendar.Locator(".fc-dayGridMonth-button").ClickAsync();
        await Assertions.Expect(calendar.Locator(".fc-daygrid").First).ToBeVisibleAsync();
        return rows;
    }

    protected Task<IReadOnlyList<ListedEntry>> ReadListViewAsync() => ReadListViewAsync(Page);

    /// <summary>The titles listed on <paramref name="date"/>.</summary>
    protected static IEnumerable<string> TitlesOn(IReadOnlyList<ListedEntry> rows, DateOnly date) =>
        rows.Where(r => r.Date == Iso(date)).Select(r => r.Title);

    // --- Accounts ---

    /// <summary>Invites a sub-account via <c>/users/invite</c> (as the owner in <see cref="E2ETestBase.Page"/>).</summary>
    protected async Task InviteAsync(string email, string displayName, string role)
    {
        await GotoInteractiveAsync("/users/invite");
        await Page.GetByPlaceholder("employee@example.com").FillAsync(email);
        await Page.GetByPlaceholder("John Doe").FillAsync(displayName);
        await Page.GetByPlaceholder("Min. 8 characters").FillAsync(Password);
        await Page.Locator("select").SelectOptionAsync(new SelectOptionValue { Label = role });
        await Button("Create Account").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));
    }
}
