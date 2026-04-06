using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class TaxRatesControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TaxRatesControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private StringContent ToJson(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOptions), Encoding.UTF8, "application/json");

    [Fact]
    public async Task GetAll_ReturnsSeededTaxRates()
    {
        var response = await Client.GetAsync("/api/tax-rates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rates = await response.Content.ReadFromJsonAsync<List<TaxRate>>(JsonOptions);
        rates.Should().HaveCount(2);
        rates.Should().Contain(r => r.Name == "Standard VAT" && r.Rate == 0.2000m);
        rates.Should().Contain(r => r.Name == "Reduced VAT" && r.Rate == 0.1000m);
    }

    [Fact]
    public async Task GetDefault_ReturnsStandardVAT()
    {
        var response = await Client.GetAsync("/api/tax-rates/default");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rate = await response.Content.ReadFromJsonAsync<TaxRate>(JsonOptions);
        rate!.Name.Should().Be("Standard VAT");
        rate.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var dto = new TaxRateDto { Name = "Zero Rate", Rate = 0m, IsDefault = false };

        var response = await Client.PostAsync("/api/tax-rates", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var rate = await response.Content.ReadFromJsonAsync<TaxRate>(JsonOptions);
        rate!.Name.Should().Be("Zero Rate");
        rate.Rate.Should().Be(0m);
    }

    [Fact]
    public async Task SetDefault_ChangesDefault()
    {
        var response = await Client.PutAsync(
            $"/api/tax-rates/{TestSeedData.TaxRateReducedId}/default", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify new default
        var getDefault = await Client.GetAsync("/api/tax-rates/default");
        var rate = await getDefault.Content.ReadFromJsonAsync<TaxRate>(JsonOptions);
        rate!.Name.Should().Be("Reduced VAT");
    }

    [Fact]
    public async Task Delete_ExistingRate_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/tax-rates/{TestSeedData.TaxRateReducedId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/tax-rates/{TestSeedData.TaxRateReducedId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var response = await Client.DeleteAsync($"/api/tax-rates/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
