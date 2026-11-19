using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// The team endpoints (AP14: the owner and managers). The tests run as the tenant owner unless a request carries
/// <see cref="TestAuthHandler.UserHeader"/>: then it is a team member whose role comes from the database.
/// </summary>
public class UsersControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public UsersControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    private async Task<TenantUser> CreateMemberAsync(string email = "manager@test.com", string displayName = "Max Kellner")
    {
        var response = await Client.PostAsync("/api/users", Json(new { Email = email, DisplayName = displayName, Password = "TestPass123!" }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return Deserialize<TenantUser>(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A team member of the test tenant (Identity user + TenantUser row); returns its account id.</summary>
    private async Task<string> SeedMemberAsync(bool active = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = Guid.NewGuid().ToString();
        var email = $"member-{id}@test.com";
        db.Users.Add(new ApplicationUser
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(),
            TenantOwnerId = CustomWebApplicationFactory.TestUserId
        });
        db.TenantUsers.Add(new TenantUser
        {
            Id = Guid.NewGuid(), ApplicationUserId = id, TenantOwnerId = CustomWebApplicationFactory.TestUserId,
            DisplayName = "Max Kellner", IsActive = active
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A request sent as the account <paramref name="userId"/> instead of the owner.</summary>
    private static HttpRequestMessage As(string userId, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : Json(body) };
        request.Headers.Add(TestAuthHandler.UserHeader, userId);
        return request;
    }

    // --- The owner manages the team ---

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoUsers()
    {
        var response = await Client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var users = Deserialize<List<TenantUser>>(body);
        users.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WithWeakPassword()
    {
        var response = await Client.PostAsync("/api/users", Json(new { Email = "weak@test.com", DisplayName = "Test", Password = "123" }));

        // Identity UserManager rejects weak passwords
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ThenGetAll_ReturnsTheMember_AsAManager()
    {
        await CreateMemberAsync("max@test.com", "Max Kellner");

        var response = await Client.GetAsync("/api/users");
        var body = await response.Content.ReadAsStringAsync();
        var users = Deserialize<List<TenantUser>>(body);
        users.Should().ContainSingle();
        users[0].DisplayName.Should().Be("Max Kellner");
        users[0].Role.Should().Be(TenantRole.Manager);
        users[0].IsActive.Should().BeTrue();
        // The unused permission column is not part of the JSON any more
        body.Should().NotContain("permissions");
    }

    /// <summary>
    /// An older client still sends <c>role</c> (here "employee", 3): System.Text.Json skips unknown properties by default,
    /// so the request works and the member is a manager.
    /// </summary>
    [Fact]
    public async Task Create_WithTheFormerRoleField_IgnoresIt_AndCreatesAManager()
    {
        var response = await Client.PostAsync("/api/users",
            Json(new { Email = "old-client@test.com", DisplayName = "Alte App", Role = 3, Password = "TestPass123!" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = Deserialize<TenantUser>(await response.Content.ReadAsStringAsync());
        created.Role.Should().Be(TenantRole.Manager);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TenantUsers.AsNoTracking().SingleAsync(tu => tu.Id == created.Id);
        row.Role.Should().Be(TenantRole.Manager);
        row.Permissions.Should().Be(Permission.None);
    }

    [Fact]
    public async Task Update_ChangesActive_AndTheDisplayName()
    {
        var created = await CreateMemberAsync();

        var response = await Client.PutAsync($"/api/users/{created.Id}", Json(new { IsActive = false, DisplayName = "  Max K.  " }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = Deserialize<TenantUser>(await response.Content.ReadAsStringAsync());
        updated.IsActive.Should().BeFalse();
        updated.DisplayName.Should().Be("Max K.");
        updated.Role.Should().Be(TenantRole.Manager);
    }

    [Fact]
    public async Task Update_WithoutADisplayName_KeepsIt()
    {
        var created = await CreateMemberAsync(displayName: "Max Kellner");

        var response = await Client.PutAsync($"/api/users/{created.Id}", Json(new { IsActive = true }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Deserialize<TenantUser>(await response.Content.ReadAsStringAsync()).DisplayName.Should().Be("Max Kellner");
    }

    [Fact]
    public async Task Update_WithAnEmptyOrTooLongDisplayName_Returns400()
    {
        var created = await CreateMemberAsync();

        (await Client.PutAsync($"/api/users/{created.Id}", Json(new { IsActive = true, DisplayName = "   " })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PutAsync($"/api/users/{created.Id}", Json(new { IsActive = true, DisplayName = new string('x', 101) })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The former <c>role</c> and <c>customPermissions</c> of an older client are ignored.</summary>
    [Fact]
    public async Task Update_WithTheFormerRoleAndPermissionFields_IgnoresThem()
    {
        var created = await CreateMemberAsync();

        var response = await Client.PutAsync($"/api/users/{created.Id}",
            Json(new { Role = 2, CustomPermissions = (long)Permission.MenusView, IsActive = true }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TenantUsers.AsNoTracking().SingleAsync(tu => tu.Id == created.Id);
        row.Role.Should().Be(TenantRole.Manager);
        row.Permissions.Should().Be(Permission.None);
    }

    [Fact]
    public async Task Delete_ExistingUser_DeactivatesUser()
    {
        var created = await CreateMemberAsync("todelete@test.com", "Delete Me");

        // Delete (deactivate)
        var deleteResponse = await Client.DeleteAsync($"/api/users/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify still exists but inactive
        var getResponse = await Client.GetAsync($"/api/users/{created.Id}");
        var getBody = await getResponse.Content.ReadAsStringAsync();
        var user = Deserialize<TenantUser>(getBody);
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Get_NonExistentUser_Returns404()
    {
        var response = await Client.GetAsync($"/api/users/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- A manager: the menus yes, the team no ---

    [Fact]
    public async Task Manager_IsForbidden_OnEveryTeamEndpoint()
    {
        var member = await CreateMemberAsync();
        var manager = await SeedMemberAsync();

        foreach (var request in new[]
                 {
                     As(manager, HttpMethod.Get, "/api/users"),
                     As(manager, HttpMethod.Get, $"/api/users/{member.Id}"),
                     As(manager, HttpMethod.Post, "/api/users", new { Email = "sneaky@test.com", DisplayName = "Sneaky", Password = "TestPass123!" }),
                     As(manager, HttpMethod.Put, $"/api/users/{member.Id}", new { IsActive = false }),
                     As(manager, HttpMethod.Delete, $"/api/users/{member.Id}")
                 })
        {
            var response = await Client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{request.Method} {request.RequestUri} is the owner's");
        }

        // Nothing changed: still one active member, no new account
        var users = Deserialize<List<TenantUser>>(await (await Client.GetAsync("/api/users")).Content.ReadAsStringAsync());
        users.Should().ContainSingle(u => u.Id == member.Id && u.IsActive);
    }

    [Fact]
    public async Task Manager_CanCreateAndEditAMenu_OfTheTenant()
    {
        var manager = await SeedMemberAsync();

        var create = await Client.SendAsync(As(manager, HttpMethod.Post, "/api/menus",
            new { Name = "Mittagskarte", Description = "Vom Manager", Theme = DesignTheme.Classic, Language = MenuLanguage.German }));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = Deserialize<Menu>(await create.Content.ReadAsStringAsync());

        var update = await Client.SendAsync(As(manager, HttpMethod.Put, $"/api/menus/{TestSeedData.Menu1Id}",
            new { Name = "Lunch Menu (neu)", Description = "Geändert", Theme = DesignTheme.Modern, Language = MenuLanguage.German }));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        // Both belong to the owner's tenant: the owner sees them
        var menus = Deserialize<List<Menu>>(await (await Client.GetAsync("/api/menus")).Content.ReadAsStringAsync());
        menus.Should().Contain(m => m.Id == created.Id && m.Name == "Mittagskarte");
        menus.Should().Contain(m => m.Id == TestSeedData.Menu1Id && m.Name == "Lunch Menu (neu)");
    }

    [Fact]
    public async Task Manager_SeesAnalytics_AndSavesTheRestaurantProfile()
    {
        var manager = await SeedMemberAsync();

        (await Client.SendAsync(As(manager, HttpMethod.Get, "/api/analytics/traffic-per-menu")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.SendAsync(As(manager, HttpMethod.Put, "/api/restaurant-profile", new { Name = "Gasthaus Zur Post" })))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        (await Client.SendAsync(As(manager, HttpMethod.Get, "/api/qrcodes")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- An inactive or removed member: nothing ---

    [Fact]
    public async Task InactiveMember_IsForbiddenEverywhere()
    {
        var inactive = await SeedMemberAsync(active: false);

        foreach (var request in new[]
                 {
                     As(inactive, HttpMethod.Get, "/api/menus"),
                     As(inactive, HttpMethod.Post, "/api/menus", new { Name = "X", Theme = DesignTheme.Modern, Language = MenuLanguage.German }),
                     As(inactive, HttpMethod.Get, "/api/qrcodes"),
                     As(inactive, HttpMethod.Get, "/api/analytics/traffic-per-menu"),
                     As(inactive, HttpMethod.Get, "/api/users")
                 })
        {
            (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{request.Method} {request.RequestUri}");
        }
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_HasNoPermissions()
    {
        var gone = Guid.NewGuid().ToString();

        (await Client.SendAsync(As(gone, HttpMethod.Get, "/api/menus"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.SendAsync(As(gone, HttpMethod.Get, "/api/users"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
