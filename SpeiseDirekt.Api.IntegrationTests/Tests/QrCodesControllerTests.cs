using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class QrCodesControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Guid NonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public QrCodesControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    // --- QRCode CRUD ---

    [Fact]
    public async Task GetAll_ReturnsSeededQrCodes()
    {
        var response = await Client.GetAsync("/api/qrcodes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var qrCodes = await response.Content.ReadFromJsonAsync<List<QRCode>>(JsonOptions);
        qrCodes.Should().HaveCountGreaterThanOrEqualTo(1);
        qrCodes.Should().Contain(q => q.Title == "Table 1");
    }

    [Fact]
    public async Task GetById_ReturnsQrCodeWithEntries()
    {
        var response = await Client.GetAsync($"/api/qrcodes/{TestSeedData.QrCodeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var qrCode = Deserialize<QRCode>(json);
        qrCode.Should().NotBeNull();
        qrCode!.Title.Should().Be("Table 1");
        qrCode.IsTimeTableBased.Should().BeTrue();
        qrCode.IsCalendarBased.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync($"/api/qrcodes/{NonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var dto = new QrCodeDto("Table 2", TestSeedData.Menu1Id, false, false);

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/qrcodes", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<QRCode>(JsonOptions);
        created.Should().NotBeNull();
        created!.Title.Should().Be("Table 2");
        created.MenuId.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task Update_ExistingQrCode_Returns200()
    {
        var dto = new QrCodeDto("Updated Table 1", TestSeedData.Menu2Id, false, true);

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/qrcodes/{TestSeedData.QrCodeId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<QRCode>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Updated Table 1");
        updated.MenuId.Should().Be(TestSeedData.Menu2Id);
        updated.IsCalendarBased.Should().BeTrue();
        updated.IsTimeTableBased.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_ExistingQrCode_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/qrcodes/{TestSeedData.QrCodeId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/qrcodes/{TestSeedData.QrCodeId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- TimeTable Entries ---

    [Fact]
    public async Task AddTimeTableEntry_ValidEntry_Returns201()
    {
        var dto = new TimeTableEntryDto(new TimeOnly(17, 0), new TimeOnly(21, 0), TestSeedData.Menu1Id);

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(
            $"/api/qrcodes/{TestSeedData.QrCodeId}/timetable-entries", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TimeTableEntry>(JsonOptions);
        created.Should().NotBeNull();
        created!.MenuId.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task AddTimeTableEntry_NonExistentQrCode_Returns404()
    {
        var dto = new TimeTableEntryDto(new TimeOnly(8, 0), new TimeOnly(10, 0), TestSeedData.Menu1Id);

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(
            $"/api/qrcodes/{NonExistentId}/timetable-entries", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveTimeTableEntry_Returns204()
    {
        var response = await Client.DeleteAsync(
            $"/api/qrcodes/{TestSeedData.QrCodeId}/timetable-entries/{TestSeedData.TimeTableEntryId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveTimeTableEntry_NonExistentEntry_Returns404()
    {
        var response = await Client.DeleteAsync(
            $"/api/qrcodes/{TestSeedData.QrCodeId}/timetable-entries/{NonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Calendar Entries ---

    [Fact]
    public async Task AddCalendarEntry_ValidEntry_Returns201()
    {
        var dto = new CalendarEntryDto(
            new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), null, TestSeedData.Menu1Id);

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(
            $"/api/qrcodes/{TestSeedData.QrCodeId}/calendar-entries", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CalendarEntry>(JsonOptions);
        created.Should().NotBeNull();
        created!.MenuId.Should().Be(TestSeedData.Menu1Id);
        created.Date.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task AddCalendarEntry_NonExistentQrCode_Returns404()
    {
        var dto = new CalendarEntryDto(
            new DateOnly(2026, 7, 1), null, DayOfWeek.Monday, TestSeedData.Menu1Id);

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(
            $"/api/qrcodes/{NonExistentId}/calendar-entries", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveCalendarEntry_Returns204()
    {
        var response = await Client.DeleteAsync(
            $"/api/qrcodes/{TestSeedData.QrCodeId}/calendar-entries/{TestSeedData.CalendarEntryId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveCalendarEntry_NonExistentEntry_Returns404()
    {
        var response = await Client.DeleteAsync(
            $"/api/qrcodes/{TestSeedData.QrCodeId}/calendar-entries/{NonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
