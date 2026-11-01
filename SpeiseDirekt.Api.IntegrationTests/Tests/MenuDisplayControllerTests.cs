using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class MenuDisplayControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MenuDisplayControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetMenuByQrCode_ValidQrCode_Returns200()
    {
        var response = await Client.GetAsync($"/api/public/menu/{TestSeedData.DirectQrCodeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = await response.Content.ReadFromJsonAsync<Menu>(JsonOptions);
        menu.Should().NotBeNull();
        menu!.Id.Should().Be(TestSeedData.Menu1Id);
        menu.Name.Should().Be("Lunch Menu");
        menu.Categories.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetMenuByQrCode_NonExistentQrCode_Returns404()
    {
        var response = await Client.GetAsync($"/api/public/menu/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RecordClick_ValidIds_Returns204()
    {
        var response = await Client.PostAsync(
            $"/api/public/menu/{TestSeedData.Menu1Id}/items/{TestSeedData.MenuItem1Id}/click",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RecordClick_InvalidMenuItemId_Returns204()
    {
        // TrackingService swallows errors, so even invalid IDs return NoContent
        var response = await Client.PostAsync(
            $"/api/public/menu/{TestSeedData.Menu1Id}/items/{Guid.NewGuid()}/click",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------------
    // Menu resolution (calendar / time table / direct)
    // Menu1 ("Lunch Menu") is used as the direct menu, Menu2 ("Dinner Menu")
    // as the calendar / time-table menu, so the source of the result is visible.
    // 2026-03-04, 2026-03-18 and 2026-02-25 are Wednesdays, 2026-03-19 is a Thursday.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetMenuByQrCode_CalendarSingleDateMatch_ReturnsCalendarMenu()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 4), TestSeedData.Menu2Id)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 4, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu2Id);
        menu.Categories.Should().ContainSingle(c => c.Id == TestSeedData.Category2Id);
    }

    [Theory]
    [InlineData("2026-03-01T00:00:00")] // first day
    [InlineData("2026-03-05T12:00:00")] // inside
    [InlineData("2026-03-10T23:59:59")] // last day (inclusive)
    public async Task GetMenuByQrCode_CalendarDateRangeMatch_ReturnsCalendarMenu(string localNow)
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 1), TestSeedData.Menu2Id, endDate: new DateOnly(2026, 3, 10))]);
        Factory.Clock.SetLocalNow(DateTime.Parse(localNow, CultureInfo.InvariantCulture));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu2Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_CalendarRecurringWeekdayMatch_ReturnsCalendarMenu()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 1), TestSeedData.Menu2Id, recurringDayOfWeek: DayOfWeek.Wednesday)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 18, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu2Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_CalendarRecurringOtherWeekday_FallsBackToDirectMenu()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 1), TestSeedData.Menu2Id, recurringDayOfWeek: DayOfWeek.Wednesday)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 19, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu1Id);
    }

    [Theory]
    [InlineData("2026-03-18T12:00:00")] // matching weekday, after EndDate
    [InlineData("2026-02-25T12:00:00")] // matching weekday, before Date
    public async Task GetMenuByQrCode_CalendarRecurringOutsideDateRange_FallsBackToDirectMenu(string localNow)
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            calendarEntries:
            [
                Calendar(new DateOnly(2026, 3, 1), TestSeedData.Menu2Id,
                    endDate: new DateOnly(2026, 3, 15), recurringDayOfWeek: DayOfWeek.Wednesday)
            ]);
        Factory.Clock.SetLocalNow(DateTime.Parse(localNow, CultureInfo.InvariantCulture));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_CalendarNoMatch_FallsBackToDirectMenu()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 4), TestSeedData.Menu2Id)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 5, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_CalendarNoMatchAndNoDirectMenu_Returns404()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: null,
            isCalendarBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 4), TestSeedData.Menu2Id)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 5, 12, 0, 0));

        var response = await Client.GetAsync($"/api/public/menu/{qrId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMenuByQrCode_SingleDateAndRecurringMatch_PrefersSingleDateEntry()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: null,
            isCalendarBased: true,
            calendarEntries:
            [
                // recurring entry has the earlier Date, the single-date entry must still win
                Calendar(new DateOnly(2026, 3, 1), TestSeedData.Menu1Id, recurringDayOfWeek: DayOfWeek.Wednesday),
                Calendar(new DateOnly(2026, 3, 4), TestSeedData.Menu2Id)
            ]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 4, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu2Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_CalendarMatch_TakesPrecedenceOverTimeTable()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isCalendarBased: true,
            isTimeTableBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 4), TestSeedData.Menu2Id)],
            timeTableEntries: [TimeTable(new TimeOnly(11, 0), new TimeOnly(14, 0), TestSeedData.Menu1Id)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 4, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu2Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_CalendarNoMatch_UsesTimeTable()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: null,
            isCalendarBased: true,
            isTimeTableBased: true,
            calendarEntries: [Calendar(new DateOnly(2026, 3, 4), TestSeedData.Menu2Id)],
            timeTableEntries: [TimeTable(new TimeOnly(11, 0), new TimeOnly(14, 0), TestSeedData.Menu1Id)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 5, 12, 0, 0));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task GetMenuByQrCode_TimeTableNoActiveWindow_Returns404EvenWithDirectMenu()
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isTimeTableBased: true,
            timeTableEntries: [TimeTable(new TimeOnly(11, 0), new TimeOnly(14, 0), TestSeedData.Menu2Id)]);
        Factory.Clock.SetLocalNow(new DateTime(2026, 3, 4, 15, 0, 0));

        var response = await Client.GetAsync($"/api/public/menu/{qrId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("22:00:00")]
    [InlineData("23:59:00")]
    [InlineData("23:59:30")] // previously in the gap between the 23:59 segment end and midnight
    [InlineData("00:00:00")]
    [InlineData("01:59:59")]
    public async Task GetMenuByQrCode_TimeTableCrossMidnight_ActiveWindow_ReturnsTimeTableMenu(string localTime)
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isTimeTableBased: true,
            timeTableEntries: [TimeTable(new TimeOnly(22, 0), new TimeOnly(2, 0), TestSeedData.Menu2Id)]);
        Factory.Clock.SetLocalNow(new DateOnly(2026, 3, 4).ToDateTime(TimeOnly.Parse(localTime, CultureInfo.InvariantCulture)));

        var menu = await GetResolvedMenuAsync(qrId);

        menu.Id.Should().Be(TestSeedData.Menu2Id);
    }

    [Theory]
    [InlineData("21:59:59")]
    [InlineData("02:00:00")]
    [InlineData("12:00:00")]
    public async Task GetMenuByQrCode_TimeTableCrossMidnight_OutsideWindow_Returns404(string localTime)
    {
        var qrId = await SeedQrCodeAsync(
            directMenuId: TestSeedData.Menu1Id,
            isTimeTableBased: true,
            timeTableEntries: [TimeTable(new TimeOnly(22, 0), new TimeOnly(2, 0), TestSeedData.Menu2Id)]);
        Factory.Clock.SetLocalNow(new DateOnly(2026, 3, 4).ToDateTime(TimeOnly.Parse(localTime, CultureInfo.InvariantCulture)));

        var response = await Client.GetAsync($"/api/public/menu/{qrId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Menu> GetResolvedMenuAsync(Guid qrCodeId)
    {
        var response = await Client.GetAsync($"/api/public/menu/{qrCodeId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = await response.Content.ReadFromJsonAsync<Menu>(JsonOptions);
        menu.Should().NotBeNull();
        return menu!;
    }

    private async Task<Guid> SeedQrCodeAsync(
        Guid? directMenuId,
        bool isCalendarBased = false,
        bool isTimeTableBased = false,
        List<CalendarEntry>? calendarEntries = null,
        List<TimeTableEntry>? timeTableEntries = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var qrCode = new QRCode
        {
            Id = Guid.NewGuid(),
            Title = "Resolution Test",
            MenuId = directMenuId,
            IsCalendarBased = isCalendarBased,
            IsTimeTableBased = isTimeTableBased,
            CalendarEntries = calendarEntries ?? [],
            TimeTableEntries = timeTableEntries ?? []
        };
        db.QRCodes.Add(qrCode);
        await db.SaveChangesAsync();

        return qrCode.Id;
    }

    // --- Public endpoint: GET /api/public/menu/{menuId}/combos ---

    [Fact]
    public async Task PublicCombos_Anonymous_ReturnsCombosOfMenuWithItemsAndPrice()
    {
        await TestSeedData.SeedCombosAsync(Factory.Services);

        var response = await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.Menu1Id}/combos"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var combos = await response.Content.ReadFromJsonAsync<List<PublicComboResponse>>(JsonOptions);
        combos.Should().ContainSingle();
        var combo = combos![0];
        combo.Id.Should().Be(TestSeedData.LunchComboId);
        combo.Name.Should().Be("Salad Combo");
        combo.Description.Should().Be("Salad with soup");
        combo.ComboPrice.Should().Be(12.00m);
        combo.TriggerMenuItem.Id.Should().Be(TestSeedData.MenuItem1Id);
        combo.TriggerMenuItem.Name.Should().Be("Caesar Salad");
        combo.TriggerMenuItem.Price.Should().Be(8.50m);
        combo.Items.Should().ContainSingle();
        combo.Items[0].Id.Should().Be(TestSeedData.MenuItem2Id);
        combo.Items[0].Name.Should().Be("Tomato Soup");
        combo.Items[0].Price.Should().Be(6.00m);
    }

    [Fact]
    public async Task PublicCombos_OtherMenu_ReturnsOnlyItsCombos()
    {
        await TestSeedData.SeedCombosAsync(Factory.Services);

        var response = await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.Menu2Id}/combos"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var combos = await response.Content.ReadFromJsonAsync<List<PublicComboResponse>>(JsonOptions);
        combos.Should().ContainSingle().Which.Id.Should().Be(TestSeedData.DinnerComboId);
    }

    [Fact]
    public async Task PublicCombos_MenuWithoutCombos_ReturnsEmptyList()
    {
        var response = await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.Menu1Id}/combos"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var combos = await response.Content.ReadFromJsonAsync<List<PublicComboResponse>>(JsonOptions);
        combos.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicCombos_NonExistentMenu_Returns404()
    {
        var response = await Client.SendAsync(AnonymousGet($"/api/public/menu/{Guid.NewGuid()}/combos"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicCombos_IgnoresTenantQueryFilters()
    {
        await TestSeedData.SeedCombosAsync(Factory.Services);

        // Move all data to another tenant: tenant-filtered queries for the test user would now find nothing.
        var otherTenant = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Menus.IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, otherTenant));
            await db.Categories.IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, otherTenant));
            await db.MenuItems.IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, otherTenant));
            await db.MenuCombos.IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, otherTenant));
            await db.MenuComboItems.IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, otherTenant));
        }

        var response = await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.Menu1Id}/combos"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var combos = await response.Content.ReadFromJsonAsync<List<PublicComboResponse>>(JsonOptions);
        var combo = combos.Should().ContainSingle().Subject;
        combo.TriggerMenuItem.Name.Should().Be("Caesar Salad");
        combo.Items.Should().ContainSingle().Which.Name.Should().Be("Tomato Soup");
    }

    private record PublicComboResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal ComboPrice { get; init; }
        public PublicComboItemResponse TriggerMenuItem { get; init; } = new();
        public List<PublicComboItemResponse> Items { get; init; } = new();
    }

    private record PublicComboItemResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal Price { get; init; }
    }

    private static CalendarEntry Calendar(
        DateOnly date, Guid menuId, DateOnly? endDate = null, DayOfWeek? recurringDayOfWeek = null) => new()
    {
        Id = Guid.NewGuid(),
        Date = date,
        EndDate = endDate,
        RecurringDayOfWeek = recurringDayOfWeek,
        MenuId = menuId
    };

    private static TimeTableEntry TimeTable(TimeOnly start, TimeOnly end, Guid menuId) => new()
    {
        Id = Guid.NewGuid(),
        StartTime = start,
        EndTime = end,
        MenuId = menuId
    };
}
