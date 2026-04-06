using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class AnalyticsControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AnalyticsControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetTrafficPerUser_Default_Returns200()
    {
        var response = await Client.GetAsync("/api/analytics/traffic-per-user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await response.Content.ReadFromJsonAsync<List<UserTrafficData>>(JsonOptions);
        data.Should().NotBeNull();
        data.Should().NotBeEmpty();
        data!.First().SessionId.Should().NotBeNullOrEmpty();
        data.First().Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTrafficPerUser_WithTimeRange_Returns200()
    {
        var response = await Client.GetAsync("/api/analytics/traffic-per-user?timeRange=Last30Days");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await response.Content.ReadFromJsonAsync<List<UserTrafficData>>(JsonOptions);
        data.Should().NotBeNull();
        data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetTrafficPerMenu_Returns200()
    {
        var response = await Client.GetAsync("/api/analytics/traffic-per-menu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await response.Content.ReadFromJsonAsync<List<MenuTrafficData>>(JsonOptions);
        data.Should().NotBeNull();
        data.Should().NotBeEmpty();
        data!.First().MenuId.Should().NotBeEmpty();
        data.First().MenuName.Should().NotBeNullOrEmpty();
        data.First().Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTrafficPerMenuItem_Returns200()
    {
        var response = await Client.GetAsync("/api/analytics/traffic-per-menuitem");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await response.Content.ReadFromJsonAsync<List<MenuItemTrafficData>>(JsonOptions);
        data.Should().NotBeNull();
        data.Should().NotBeEmpty();
        data!.First().MenuItemId.Should().NotBeEmpty();
        data.First().MenuItemName.Should().NotBeNullOrEmpty();
        data.First().Data.Should().NotBeNull();
    }
}
