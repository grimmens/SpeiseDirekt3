using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class AllergensControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AllergensControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    // --- GET all ---

    [Fact]
    public async Task GetAll_WithMenuId_ReturnsAllergensForThatMenu()
    {
        var response = await Client.GetAsync($"/api/allergens?menuId={TestSeedData.Menu1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var allergens = await response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);
        allergens.Should().HaveCount(2);
        allergens.Should().Contain(a => a.Code == "A" && a.Name == "Gluten");
        allergens.Should().Contain(a => a.Code == "G" && a.Name == "Milk");
    }

    [Fact]
    public async Task GetAll_WithMenuId_ReturnsOrderedByCode()
    {
        var response = await Client.GetAsync($"/api/allergens?menuId={TestSeedData.Menu1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var allergens = await response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);
        allergens.Should().NotBeEmpty();
        allergens!.Select(a => a.Code).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetAll_WithoutMenuId_Returns400()
    {
        var response = await Client.GetAsync("/api/allergens");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAll_Menu2_ReturnsOnlyMenu2Allergens()
    {
        var response = await Client.GetAsync($"/api/allergens?menuId={TestSeedData.Menu2Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var allergens = await response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);
        allergens.Should().HaveCount(1);
        allergens.Should().Contain(a => a.Code == "D" && a.Name == "Fish");
    }

    [Fact]
    public async Task GetAll_AllergensFromOneMenu_DoNotAppearInAnother()
    {
        var menu1Response = await Client.GetAsync($"/api/allergens?menuId={TestSeedData.Menu1Id}");
        var menu2Response = await Client.GetAsync($"/api/allergens?menuId={TestSeedData.Menu2Id}");

        var menu1Allergens = await menu1Response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);
        var menu2Allergens = await menu2Response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);

        var menu1Ids = menu1Allergens!.Select(a => a.Id).ToHashSet();
        var menu2Ids = menu2Allergens!.Select(a => a.Id).ToHashSet();

        menu1Ids.Intersect(menu2Ids).Should().BeEmpty();
    }

    // --- GET by ID ---

    [Fact]
    public async Task GetById_ReturnsAllergen()
    {
        var response = await Client.GetAsync($"/api/allergens/{TestSeedData.AllergenGlutenId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var allergen = await response.Content.ReadFromJsonAsync<AllergenResponse>(JsonOptions);
        allergen.Should().NotBeNull();
        allergen!.Code.Should().Be("A");
        allergen.Name.Should().Be("Gluten");
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.GetAsync($"/api/allergens/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- POST ---

    [Fact]
    public async Task Create_ValidAllergen_Returns201()
    {
        var dto = new { Code = "C", Name = "Eggs", MenuId = TestSeedData.Menu1Id };
        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/allergens", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<AllergenResponse>(JsonOptions);
        created.Should().NotBeNull();
        created!.Code.Should().Be("C");
        created.Name.Should().Be("Eggs");
    }

    [Fact]
    public async Task Create_DuplicateCodeInSameMenu_Returns400()
    {
        var dto = new { Code = "A", Name = "Duplicate Gluten", MenuId = TestSeedData.Menu1Id };
        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/allergens", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_SameCodeInDifferentMenu_Returns201()
    {
        // Code "A" exists in Menu1, but should be allowed in Menu2
        var dto = new { Code = "A", Name = "Gluten (Menu2)", MenuId = TestSeedData.Menu2Id };
        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/allergens", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // --- PUT ---

    [Fact]
    public async Task Update_ExistingAllergen_Returns200()
    {
        var dto = new { Code = "A", Name = "Gluten (updated)", MenuId = TestSeedData.Menu1Id };
        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/allergens/{TestSeedData.AllergenGlutenId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<AllergenResponse>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Gluten (updated)");
    }

    [Fact]
    public async Task Update_DuplicateCode_Returns400()
    {
        // Try to change Milk's code to "A" which is already used by Gluten in the same menu
        var dto = new { Code = "A", Name = "Milk", MenuId = TestSeedData.Menu1Id };
        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/allergens/{TestSeedData.AllergenMilkId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var dto = new { Code = "X", Name = "Ghost", MenuId = TestSeedData.Menu1Id };
        var content = new StringContent(
            JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PutAsync($"/api/allergens/{nonExistentId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- DELETE ---

    [Fact]
    public async Task Delete_ExistingAllergen_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/allergens/{TestSeedData.AllergenFishId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await Client.GetAsync($"/api/allergens/{TestSeedData.AllergenFishId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var nonExistentId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await Client.DeleteAsync($"/api/allergens/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private record AllergenResponse
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }
}
