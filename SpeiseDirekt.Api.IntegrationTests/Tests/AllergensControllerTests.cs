using System.Net;
using System.Net.Http.Json;
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

    [Fact]
    public async Task GetAll_ReturnsSeededAllergens()
    {
        var response = await Client.GetAsync("/api/allergens");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var allergens = await response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);
        allergens.Should().HaveCount(3);
        allergens.Should().Contain(a => a.Code == "A" && a.Name == "Gluten");
        allergens.Should().Contain(a => a.Code == "D" && a.Name == "Fish");
        allergens.Should().Contain(a => a.Code == "G" && a.Name == "Milk");
    }

    [Fact]
    public async Task GetAll_ReturnsAllergensOrderedByCode()
    {
        var response = await Client.GetAsync("/api/allergens");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var allergens = await response.Content.ReadFromJsonAsync<List<AllergenResponse>>(JsonOptions);
        allergens.Should().NotBeEmpty();
        allergens!.Select(a => a.Code).Should().BeInAscendingOrder();
    }

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

    private record AllergenResponse
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }
}
