using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <c>GET/PUT /api/restaurant-profile</c> (own tenant) and the anonymous <c>GET /api/public/menu/{menuId}/restaurant</c>.
/// </summary>
public class RestaurantProfileControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RestaurantProfileControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private static RestaurantProfileDto FullProfile() => new()
    {
        Name = "  Gasthaus Zur Post  ",
        Description = "Wirtshaus seit 1890",
        Phone = "+43 1 234 56 78",
        Email = "info@example.com",
        Website = "https://example.com",
        InstagramUrl = "https://instagram.com/zurpost",
        FacebookUrl = "https://facebook.com/zurpost",
        GoogleReviewUrl = "https://g.page/r/zurpost/review",
        Address = "Hauptplatz 1\r\n1010 Wien",
        OpeningHours = "Mo–Fr 11:00–22:00\nSa 10:00–23:00"
    };

    private Task<HttpResponseMessage> PutAsync(object dto) =>
        Client.PutAsJsonAsync("/api/restaurant-profile", dto, JsonOptions);

    [Fact]
    public async Task Get_WithoutProfile_Returns404()
    {
        var response = await Client.GetAsync("/api/restaurant-profile");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_CreatesThenUpdates_AndValuesAreNormalized()
    {
        var created = await PutAsync(FullProfile());

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var profile = (await created.Content.ReadFromJsonAsync<RestaurantProfile>(JsonOptions))!;
        profile.Name.Should().Be("Gasthaus Zur Post");
        profile.Address.Should().Be("Hauptplatz 1\n1010 Wien");
        profile.OpeningHours.Should().Be("Mo–Fr 11:00–22:00\nSa 10:00–23:00");
        profile.ApplicationUserId.Should().Be(Guid.Parse(CustomWebApplicationFactory.TestUserId));

        // Second PUT updates the same row; empty strings clear optional fields
        var updated = await PutAsync(FullProfile() with { Name = "Zur Post", Phone = "", Website = "  " });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = (await Client.GetFromJsonAsync<RestaurantProfile>("/api/restaurant-profile", JsonOptions))!;
        fetched.Id.Should().Be(profile.Id);
        fetched.Name.Should().Be("Zur Post");
        fetched.Phone.Should().BeNull();
        fetched.Website.Should().BeNull();
        fetched.Email.Should().Be("info@example.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.RestaurantProfiles.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("website", "example.com")]
    [InlineData("instagramUrl", "instagram.com/zurpost")]
    [InlineData("facebookUrl", "ftp://facebook.com/zurpost")]
    [InlineData("googleReviewUrl", "javascript:alert(1)")]
    public async Task Put_RelativeOrNonHttpUrl_Returns400(string field, string value)
    {
        var dto = JsonSerializer.SerializeToNode(FullProfile(), JsonOptions)!.AsObject();
        dto[field] = value;

        var response = await PutAsync(dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("https://");
        (await Client.GetAsync("/api/restaurant-profile")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_MissingOrBlankName_InvalidEmail_TooLongDescription_Return400()
    {
        (await PutAsync(FullProfile() with { Name = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(FullProfile() with { Name = "   " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(FullProfile() with { Email = "kein-mail" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(FullProfile() with { Description = new string('x', 1001) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Client.GetAsync("/api/restaurant-profile")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_ImagePaths_MustReferenceAnUploadedImage_NullKeeps_EmptyRemoves()
    {
        var logo = $"image/{TestSeedData.ImageId}";

        (await PutAsync(FullProfile() with { LogoImagePath = "https://evil.example/logo.png" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(FullProfile() with { LogoImagePath = $"image/{Guid.NewGuid()}" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await PutAsync(FullProfile() with { LogoImagePath = logo, CoverImagePath = logo }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Omitted (null) keeps both images
        (await PutAsync(FullProfile())).StatusCode.Should().Be(HttpStatusCode.OK);
        var kept = (await Client.GetFromJsonAsync<RestaurantProfile>("/api/restaurant-profile", JsonOptions))!;
        kept.LogoImagePath.Should().Be(logo);
        kept.CoverImagePath.Should().Be(logo);

        // Empty string removes one
        (await PutAsync(FullProfile() with { CoverImagePath = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var removed = (await Client.GetFromJsonAsync<RestaurantProfile>("/api/restaurant-profile", JsonOptions))!;
        removed.LogoImagePath.Should().Be(logo);
        removed.CoverImagePath.Should().BeNull();
    }

    [Fact]
    public async Task OtherTenantsProfile_IsNeitherReadNorOverwritten()
    {
        // A profile of another tenant: created as the test user, then moved to the other tenant
        var otherTenant = Guid.NewGuid();
        await SeedProfileAsync("Fremdes Lokal", otherTenant);

        (await Client.GetAsync("/api/restaurant-profile")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await PutAsync(FullProfile())).StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var all = await db.RestaurantProfiles.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        all.Should().HaveCount(2);
        all.Single(p => p.ApplicationUserId == otherTenant).Name.Should().Be("Fremdes Lokal");
    }

    // --- Public endpoint ---

    [Fact]
    public async Task Public_ReturnsTheMenuOwnersProfile_Anonymously_WithImageUrls()
    {
        (await PutAsync(FullProfile() with { LogoImagePath = $"image/{TestSeedData.ImageId}" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.Menu1Id}/restaurant"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("applicationUserId").And.NotContain(CustomWebApplicationFactory.TestUserId);
        var profile = Deserialize<PublicRestaurantProfileDto>(json);
        profile.Name.Should().Be("Gasthaus Zur Post");
        profile.OpeningHours.Should().Be("Mo–Fr 11:00–22:00\nSa 10:00–23:00");
        profile.LogoImageUrl.Should().Be($"/api/images/{TestSeedData.ImageId}");
        profile.CoverImageUrl.Should().BeNull();

        // The image URL is served anonymously
        (await Client.SendAsync(AnonymousGet(profile.LogoImageUrl!))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Public_NoProfile_OrUnknownMenu_Returns404()
    {
        (await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.Menu1Id}/restaurant")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await PutAsync(FullProfile())).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Client.SendAsync(AnonymousGet($"/api/public/menu/{Guid.NewGuid()}/restaurant")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Public_IgnoresTenantQueryFilters_AndPicksTheOwnerOfTheMenu()
    {
        // Menu 1 and a profile belong to another tenant; the test user's own profile must not be returned for it
        var otherTenant = Guid.NewGuid();
        await SeedProfileAsync("Lokal des Menü-Besitzers", otherTenant);
        (await PutAsync(FullProfile())).StatusCode.Should().Be(HttpStatusCode.Created);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Menus.IgnoreQueryFilters().Where(m => m.Id == TestSeedData.Menu1Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, otherTenant));
        }

        var other = Deserialize<PublicRestaurantProfileDto>(await (await Client.SendAsync(
            AnonymousGet($"/api/public/menu/{TestSeedData.Menu1Id}/restaurant"))).Content.ReadAsStringAsync());
        other.Name.Should().Be("Lokal des Menü-Besitzers");

        var own = Deserialize<PublicRestaurantProfileDto>(await (await Client.SendAsync(
            AnonymousGet($"/api/public/menu/{TestSeedData.Menu2Id}/restaurant"))).Content.ReadAsStringAsync());
        own.Name.Should().Be("Gasthaus Zur Post");
    }

    private async Task SeedProfileAsync(string name, Guid tenant)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = Guid.NewGuid();
        db.RestaurantProfiles.Add(new RestaurantProfile { Id = id, Name = name });
        await db.SaveChangesAsync();
        await db.RestaurantProfiles.IgnoreQueryFilters().Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ApplicationUserId, tenant));
    }
}
