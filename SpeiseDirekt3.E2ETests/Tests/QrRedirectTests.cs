using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Anonymous QR code resolution at <c>/qr/{id}</c>: direct menu, time table (fixed clock, also across
/// midnight), calendar entries and their fallbacks. Every test seeds its own menus and QR code.
/// </summary>
public class QrRedirectTests : E2ETestBase
{
    public QrRedirectTests(E2EFixture fixture) : base(fixture) { }

    private static readonly Guid Owner = E2ESeedData.OwnerId;

    // --- Seeding helpers ---

    /// <summary>A menu with one category and one item, so the complex display renders it.</summary>
    private async Task<Guid> SeedMenuAsync(string name)
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(Owner, db =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = $"{name} Kategorie", MenuId = menuId };
            db.Menus.Add(new Menu
            {
                Id = menuId,
                Name = name,
                Description = $"{name} Beschreibung",
                Language = MenuLanguage.German,
                Categories = new List<Category> { category }
            });
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(), Name = $"{name} Gericht", Description = "Test", Price = 9.90m, CategoryId = category.Id
            });
            return Task.CompletedTask;
        });
        return menuId;
    }

    private async Task<Guid> SeedQrAsync(
        Guid? directMenuId = null,
        bool timeTable = false,
        bool calendar = false,
        IEnumerable<(TimeOnly Start, TimeOnly End, Guid MenuId)>? windows = null,
        IEnumerable<CalendarEntry>? calendarEntries = null)
    {
        var qrId = Guid.NewGuid();
        await App.SeedAsync(Owner, db =>
        {
            db.QRCodes.Add(new QRCode
            {
                Id = qrId,
                Title = "E2E QR",
                MenuId = directMenuId,
                IsTimeTableBased = timeTable,
                IsCalendarBased = calendar,
                TimeTableEntries = (windows ?? []).Select(w => new TimeTableEntry
                {
                    Id = Guid.NewGuid(), StartTime = w.Start, EndTime = w.End, MenuId = w.MenuId, QRCodeId = qrId
                }).ToList(),
                CalendarEntries = (calendarEntries ?? []).Select(e => { e.QRCodeId = qrId; return e; }).ToList()
            });
            return Task.CompletedTask;
        });
        return qrId;
    }

    private static CalendarEntry Calendar(DateOnly date, Guid menuId, DateOnly? endDate = null, DayOfWeek? weekly = null) =>
        new() { Id = Guid.NewGuid(), Date = date, EndDate = endDate, RecurringDayOfWeek = weekly, MenuId = menuId };

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    /// <summary>Opens <c>/qr/{id}</c> and asserts that the menu named <paramref name="menuName"/> is shown.</summary>
    private async Task ExpectQrShowsMenuAsync(Guid qrId, string menuName)
    {
        await OpenPublicMenuPageAsync(Page, $"/qr/{qrId}");
        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync(menuName);
        await Assertions.Expect(Page.Locator("h3", new() { HasTextString = $"{menuName} Gericht" }).First).ToBeVisibleAsync();
    }

    private Task SetClockAsync(DateTime localNow)
    {
        App.Clock.SetLocalNow(localNow);
        return Task.CompletedTask;
    }

    // --- 1. Direct menu / unknown QR ---

    [Fact]
    public async Task DirectQr_ShowsItsMenu()
    {
        var menuId = await SeedMenuAsync("Direktmenü");
        var qrId = await SeedQrAsync(directMenuId: menuId);

        await ExpectQrShowsMenuAsync(qrId, "Direktmenü");
    }

    [Fact]
    public async Task UnknownQr_ShowsInvalidQrPage_NoServerError()
    {
        var response = await Page.GotoAsync($"/qr/{Guid.NewGuid()}");

        response!.Status.Should().Be(200);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Invalid QR code" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("This code does not exist or has been deleted.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task MalformedQrId_IsNotFound_NoServerError()
    {
        var response = await Page.GotoAsync("/qr/not-a-guid");

        response!.Status.Should().BeLessThan(500);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Invalid QR code" })).ToHaveCountAsync(0);
    }

    // --- 2. Time table ---

    private async Task<Guid> SeedTimeTableQrAsync(Guid lunch, Guid evening, Guid night) =>
        await SeedQrAsync(timeTable: true, windows: new[]
        {
            (T(11), T(14), lunch),
            (T(17), T(22), evening),
            (T(22), T(2), night) // across midnight
        });

    [Theory]
    [InlineData(11, 0, "Mittag")]
    [InlineData(12, 30, "Mittag")]
    [InlineData(18, 0, "Abend")]
    [InlineData(21, 59, "Abend")]
    [InlineData(22, 0, "Nacht")]
    [InlineData(23, 30, "Nacht")]
    [InlineData(1, 30, "Nacht")]
    public async Task TimeTableQr_ShowsMenuOfActiveWindow(int hour, int minute, string expectedPrefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var lunch = await SeedMenuAsync($"Mittag {suffix}");
        var evening = await SeedMenuAsync($"Abend {suffix}");
        var night = await SeedMenuAsync($"Nacht {suffix}");
        var qrId = await SeedTimeTableQrAsync(lunch, evening, night);

        await SetClockAsync(new DateTime(2026, 11, 3, hour, minute, 0));

        await ExpectQrShowsMenuAsync(qrId, $"{expectedPrefix} {suffix}");
    }

    [Theory]
    [InlineData(10, 59)] // before lunch
    [InlineData(14, 0)]  // end is exclusive
    [InlineData(15, 30)] // gap between lunch and evening
    [InlineData(2, 0)]   // end of the night window (exclusive)
    public async Task TimeTableQr_OutsideEveryWindow_ShowsNoMenu_NoError(int hour, int minute)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var lunch = await SeedMenuAsync($"Mittag {suffix}");
        var evening = await SeedMenuAsync($"Abend {suffix}");
        var night = await SeedMenuAsync($"Nacht {suffix}");
        var qrId = await SeedTimeTableQrAsync(lunch, evening, night);

        await SetClockAsync(new DateTime(2026, 11, 3, hour, minute, 0));
        var response = await Page.GotoAsync($"/qr/{qrId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Current behavior: the QR code exists but no menu is active → the page stays empty
        // (no menu, no "Invalid QR code", no server error).
        response!.Status.Should().Be(200);
        await Assertions.Expect(Page.GetByText(suffix)).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Invalid QR code" })).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByText("loading…")).ToHaveCountAsync(0);
    }

    // --- 3. Calendar ---

    [Fact]
    public async Task CalendarQr_DateEntry_BeatsTimeTable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync($"Tageskarte {suffix}");
        var christmas = await SeedMenuAsync($"Weihnachten {suffix}");
        var qrId = await SeedQrAsync(timeTable: true, calendar: true,
            windows: new[] { (T(6), T(23), regular) },
            calendarEntries: new[] { Calendar(new DateOnly(2026, 12, 24), christmas) });

        await SetClockAsync(new DateTime(2026, 12, 24, 12, 0, 0));
        await ExpectQrShowsMenuAsync(qrId, $"Weihnachten {suffix}");
    }

    [Fact]
    public async Task CalendarQr_DateRange_AppliesOnEveryDayOfTheRange()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync($"Tageskarte {suffix}");
        var festival = await SeedMenuAsync($"Festwoche {suffix}");
        var qrId = await SeedQrAsync(directMenuId: regular, calendar: true,
            calendarEntries: new[] { Calendar(new DateOnly(2026, 11, 10), festival, endDate: new DateOnly(2026, 11, 12)) });

        await SetClockAsync(new DateTime(2026, 11, 12, 20, 0, 0));
        await ExpectQrShowsMenuAsync(qrId, $"Festwoche {suffix}");
    }

    [Fact]
    public async Task CalendarQr_WeeklyRecurringEntry_AppliesOnThatWeekday()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync($"Tageskarte {suffix}");
        var monday = await SeedMenuAsync($"Montagsmenü {suffix}");
        var qrId = await SeedQrAsync(directMenuId: regular, calendar: true,
            calendarEntries: new[] { Calendar(new DateOnly(2026, 1, 5), monday, weekly: DayOfWeek.Monday) });

        await SetClockAsync(new DateTime(2026, 11, 2, 12, 0, 0)); // Monday
        await ExpectQrShowsMenuAsync(qrId, $"Montagsmenü {suffix}");
    }

    [Fact]
    public async Task CalendarQr_SingleDateEntry_BeatsWeeklyEntryOnTheSameDay()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync($"Tageskarte {suffix}");
        var monday = await SeedMenuAsync($"Montagsmenü {suffix}");
        var special = await SeedMenuAsync($"Sonderaktion {suffix}");
        var qrId = await SeedQrAsync(directMenuId: regular, calendar: true, calendarEntries: new[]
        {
            Calendar(new DateOnly(2026, 1, 5), monday, weekly: DayOfWeek.Monday),
            Calendar(new DateOnly(2026, 11, 9), special) // also a Monday
        });

        await SetClockAsync(new DateTime(2026, 11, 9, 12, 0, 0));
        await ExpectQrShowsMenuAsync(qrId, $"Sonderaktion {suffix}");
    }

    [Fact]
    public async Task CalendarQr_NoMatchingEntry_FallsBackToTimeTable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync($"Tageskarte {suffix}");
        var monday = await SeedMenuAsync($"Montagsmenü {suffix}");
        var qrId = await SeedQrAsync(timeTable: true, calendar: true,
            windows: new[] { (T(6), T(23), regular) },
            calendarEntries: new[] { Calendar(new DateOnly(2026, 1, 5), monday, weekly: DayOfWeek.Monday) });

        await SetClockAsync(new DateTime(2026, 11, 3, 12, 0, 0)); // Tuesday
        await ExpectQrShowsMenuAsync(qrId, $"Tageskarte {suffix}");
    }

    [Fact]
    public async Task CalendarQr_NoMatchingEntry_FallsBackToDirectMenu()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var regular = await SeedMenuAsync($"Tageskarte {suffix}");
        var christmas = await SeedMenuAsync($"Weihnachten {suffix}");
        var qrId = await SeedQrAsync(directMenuId: regular, calendar: true,
            calendarEntries: new[] { Calendar(new DateOnly(2026, 12, 24), christmas) });

        await SetClockAsync(new DateTime(2026, 12, 23, 12, 0, 0));
        await ExpectQrShowsMenuAsync(qrId, $"Tageskarte {suffix}");
    }
}
