using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class MenusControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MenusControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAll_ReturnsSeededMenus()
    {
        var response = await Client.GetAsync("/api/menus");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var menus = await response.Content.ReadFromJsonAsync<List<Menu>>(JsonOptions);
        menus.Should().HaveCount(2);
        menus.Should().Contain(m => m.Name == "Lunch Menu");
        menus.Should().Contain(m => m.Name == "Dinner Menu");
    }

    [Fact]
    public async Task GetById_ReturnsMenuWithCategories()
    {
        var response = await Client.GetAsync($"/api/menus/{TestSeedData.Menu1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = await response.Content.ReadFromJsonAsync<Menu>(JsonOptions);
        menu.Should().NotBeNull();
        menu!.Name.Should().Be("Lunch Menu");
        menu.Categories.Should().NotBeNullOrEmpty();
        menu.Categories.Should().Contain(c => c.Name == "Starters");
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.GetAsync($"/api/menus/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var dto = new MenuDto
        {
            Name = "Brunch Menu",
            Description = "Weekend brunch specials",
            Theme = DesignTheme.Modern,
            Language = MenuLanguage.English
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/menus", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<Menu>(JsonOptions);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Brunch Menu");
        created.Description.Should().Be("Weekend brunch specials");
    }

    [Fact]
    public async Task Create_InvalidDto_Returns400()
    {
        var dto = new { Name = "", Description = "" };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/menus", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ExistingMenu_Returns200()
    {
        var dto = new MenuDto
        {
            Name = "Updated Lunch Menu",
            Description = "Updated daily lunch specials",
            Theme = DesignTheme.Elegant,
            Language = MenuLanguage.French
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/menus/{TestSeedData.Menu1Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<Menu>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Lunch Menu");
        updated.Description.Should().Be("Updated daily lunch specials");
    }

    [Fact]
    public async Task Update_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var dto = new MenuDto
        {
            Name = "Ghost Menu",
            Description = "Does not exist"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/menus/{nonExistentId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ExistingMenu_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/menus/{TestSeedData.Menu2Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await Client.GetAsync($"/api/menus/{TestSeedData.Menu2Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.DeleteAsync($"/api/menus/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
