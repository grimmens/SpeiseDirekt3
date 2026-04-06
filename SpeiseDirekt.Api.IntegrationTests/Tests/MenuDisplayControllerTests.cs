using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
}
