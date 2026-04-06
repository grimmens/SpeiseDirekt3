using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class MenuItemsControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MenuItemsControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAll_ReturnsSeededMenuItems()
    {
        var response = await Client.GetAsync("/api/menuitems");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<MenuItem>>(JsonOptions);
        items.Should().HaveCount(3);
        items.Should().Contain(i => i.Name == "Caesar Salad");
        items.Should().Contain(i => i.Name == "Tomato Soup");
        items.Should().Contain(i => i.Name == "Grilled Salmon");
    }

    [Fact]
    public async Task GetAll_FilterByCategoryId_ReturnsFiltered()
    {
        var response = await Client.GetAsync($"/api/menuitems?categoryId={TestSeedData.Category1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<MenuItem>>(JsonOptions);
        items.Should().HaveCount(2);
        items.Should().Contain(i => i.Name == "Caesar Salad");
        items.Should().Contain(i => i.Name == "Tomato Soup");
    }

    [Fact]
    public async Task GetById_ReturnsMenuItem()
    {
        var response = await Client.GetAsync($"/api/menuitems/{TestSeedData.MenuItem1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await response.Content.ReadFromJsonAsync<MenuItem>(JsonOptions);
        item.Should().NotBeNull();
        item!.Name.Should().Be("Caesar Salad");
        item.Price.Should().Be(8.50m);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.GetAsync($"/api/menuitems/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var dto = new MenuItemDto
        {
            Name = "Fish and Chips",
            Description = "Classic British dish",
            Allergens = "Fish, Gluten",
            Price = 14.50m,
            CategoryId = TestSeedData.Category1Id
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/menuitems", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<MenuItem>(JsonOptions);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Fish and Chips");
        created.Price.Should().Be(14.50m);
        created.CategoryId.Should().Be(TestSeedData.Category1Id);
    }

    [Fact]
    public async Task Create_InvalidCategoryId_Returns400()
    {
        var dto = new MenuItemDto
        {
            Name = "Ghost Item",
            Description = "Should not be created",
            Allergens = "",
            Price = 10.00m,
            CategoryId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/menuitems", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ExistingMenuItem_Returns200()
    {
        var dto = new MenuItemDto
        {
            Name = "Updated Caesar Salad",
            Description = "Fresh romaine lettuce with caesar dressing",
            Allergens = "Dairy, Gluten",
            Price = 9.99m,
            CategoryId = TestSeedData.Category1Id
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/menuitems/{TestSeedData.MenuItem1Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<MenuItem>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Caesar Salad");
        updated.Price.Should().Be(9.99m);
    }

    [Fact]
    public async Task Update_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var dto = new MenuItemDto
        {
            Name = "Ghost Item",
            Description = "Does not exist",
            Allergens = "",
            Price = 10.00m,
            CategoryId = TestSeedData.Category1Id
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/menuitems/{nonExistentId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ExistingMenuItem_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/menuitems/{TestSeedData.MenuItem3Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await Client.GetAsync($"/api/menuitems/{TestSeedData.MenuItem3Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.DeleteAsync($"/api/menuitems/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
