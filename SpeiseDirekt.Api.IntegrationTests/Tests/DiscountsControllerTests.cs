using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class DiscountsControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DiscountsControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private StringContent ToJson(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOptions), Encoding.UTF8, "application/json");

    [Fact]
    public async Task GetAll_ReturnsSeededDiscount()
    {
        var response = await Client.GetAsync("/api/discounts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var discounts = await response.Content.ReadFromJsonAsync<List<Discount>>(JsonOptions);
        discounts.Should().HaveCount(1);
        discounts![0].Code.Should().Be("SAVE10");
    }

    [Fact]
    public async Task GetById_ReturnsDiscount()
    {
        var response = await Client.GetAsync($"/api/discounts/{TestSeedData.DiscountId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var discount = await response.Content.ReadFromJsonAsync<Discount>(JsonOptions);
        discount!.Code.Should().Be("SAVE10");
        discount.Type.Should().Be(DiscountType.Percentage);
        discount.Value.Should().Be(10m);
    }

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var dto = new DiscountDto
        {
            Code = "FLAT5",
            Description = "5 EUR off",
            Type = DiscountType.FixedAmount,
            Value = 5m,
            IsActive = true
        };

        var response = await Client.PostAsync("/api/discounts", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var discount = await response.Content.ReadFromJsonAsync<Discount>(JsonOptions);
        discount!.Code.Should().Be("FLAT5");
        discount.Type.Should().Be(DiscountType.FixedAmount);
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var dto = new DiscountDto
        {
            Code = "SAVE20",
            Description = "Updated to 20%",
            Type = DiscountType.Percentage,
            Value = 20m,
            IsActive = true
        };

        var response = await Client.PutAsync($"/api/discounts/{TestSeedData.DiscountId}", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var discount = await response.Content.ReadFromJsonAsync<Discount>(JsonOptions);
        discount!.Code.Should().Be("SAVE20");
        discount.Value.Should().Be(20m);
    }

    [Fact]
    public async Task Delete_Returns204()
    {
        var response = await Client.DeleteAsync($"/api/discounts/{TestSeedData.DiscountId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Validate_ValidCode_ReturnsTrue()
    {
        var dto = new ValidateDiscountDto { Code = "SAVE10", OrderSubTotal = 20m };
        var response = await Client.PostAsync("/api/discounts/validate", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        result.GetProperty("isValid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InvalidCode_ReturnsFalseWithMessage()
    {
        var dto = new ValidateDiscountDto { Code = "NONEXISTENT", OrderSubTotal = 20m };
        var response = await Client.PostAsync("/api/discounts/validate", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
        result.GetProperty("errorMessage").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Validate_BelowMinOrderAmount_ReturnsFalse()
    {
        var dto = new ValidateDiscountDto { Code = "SAVE10", OrderSubTotal = 2m };
        var response = await Client.PostAsync("/api/discounts/validate", ToJson(dto));

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
    }
}
