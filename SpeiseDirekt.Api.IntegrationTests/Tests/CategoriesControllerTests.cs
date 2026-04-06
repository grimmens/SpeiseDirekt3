using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class CategoriesControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CategoriesControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAll_ReturnsSeededCategories()
    {
        var response = await Client.GetAsync("/api/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<List<Category>>(JsonOptions);
        categories.Should().HaveCount(2);
        categories.Should().Contain(c => c.Name == "Starters");
        categories.Should().Contain(c => c.Name == "Main Courses");
    }

    [Fact]
    public async Task GetAll_FilterByMenuId_ReturnsFiltered()
    {
        var response = await Client.GetAsync($"/api/categories?menuId={TestSeedData.Menu1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<List<Category>>(JsonOptions);
        categories.Should().HaveCount(1);
        categories![0].Name.Should().Be("Starters");
    }

    [Fact]
    public async Task GetById_ReturnsCategoryWithMenuItems()
    {
        var response = await Client.GetAsync($"/api/categories/{TestSeedData.Category1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var category = await response.Content.ReadFromJsonAsync<Category>(JsonOptions);
        category.Should().NotBeNull();
        category!.Name.Should().Be("Starters");
        category.MenuItems.Should().NotBeNullOrEmpty();
        category.MenuItems.Should().Contain(mi => mi.Name == "Caesar Salad");
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.GetAsync($"/api/categories/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var dto = new CategoryDto
        {
            Name = "Desserts",
            MenuId = TestSeedData.Menu1Id
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/categories", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<Category>(JsonOptions);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Desserts");
        created.MenuId.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task Create_InvalidMenuId_Returns400()
    {
        var dto = new CategoryDto
        {
            Name = "Ghost Category",
            MenuId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/categories", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ExistingCategory_Returns200()
    {
        var dto = new CategoryDto
        {
            Name = "Updated Starters",
            MenuId = TestSeedData.Menu1Id
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/categories/{TestSeedData.Category1Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<Category>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Starters");
    }

    [Fact]
    public async Task Update_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var dto = new CategoryDto
        {
            Name = "Ghost Category",
            MenuId = TestSeedData.Menu1Id
        };

        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/categories/{nonExistentId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ExistingCategory_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/categories/{TestSeedData.Category2Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await Client.GetAsync($"/api/categories/{TestSeedData.Category2Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.DeleteAsync($"/api/categories/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
