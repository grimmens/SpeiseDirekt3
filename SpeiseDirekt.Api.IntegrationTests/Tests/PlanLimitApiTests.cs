using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// The free tier at the API: a tenant without <c>PaidTenant</c> creates one menu and one QR code, the next create is a
/// 400 with the limit from Plans:Free; a paid tenant has no limit; content above the limit stays editable. A free
/// tenant is a second owner (requests sent as it via <see cref="TestAuthHandler.UserHeader"/>) or the test tenant
/// without its role.
/// </summary>
public class PlanLimitApiTests : BaseIntegrationTest
{
    private const string MenuLimitText = "Menu limit reached (1). Upgrade your plan to add more menus.";
    private const string QrCodeLimitText = "QR code limit reached (1). Upgrade your plan to add more QR codes.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PlanLimitApiTests(CustomWebApplicationFactory factory) : base(factory) { }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    private static object NewMenu(string name) =>
        new { Name = name, Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German };

    /// <summary>A request as <paramref name="userId"/>; null = the seeded test tenant.</summary>
    private static HttpRequestMessage Post(string? userId, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = Json(body) };
        if (userId is not null)
            request.Headers.Add(TestAuthHandler.UserHeader, userId);
        return request;
    }

    private async Task<(int Menus, int QrCodes)> CountAsync(string ownerId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = Guid.Parse(ownerId);
        return (await db.Menus.IgnoreQueryFilters().CountAsync(m => m.ApplicationUserId == owner),
            await db.QRCodes.IgnoreQueryFilters().CountAsync(q => q.ApplicationUserId == owner));
    }

    [Fact]
    public async Task AFreeTenant_CreatesItsFirstMenu_AndGets400ForTheSecond()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "free-menus@test.com");

        var first = await Client.SendAsync(Post(owner, "/api/menus", NewMenu("Mittagskarte")));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = Deserialize<Menu>(await first.Content.ReadAsStringAsync());
        created.ApplicationUserId.Should().Be(Guid.Parse(owner), "the menu belongs to the free tenant");

        var second = await Client.SendAsync(Post(owner, "/api/menus", NewMenu("Abendkarte")));
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync()).Should().Be(MenuLimitText);

        (await CountAsync(owner)).Should().Be((1, 0));
    }

    [Fact]
    public async Task AFreeTenant_CreatesItsFirstQrCode_AndGets400ForTheSecond()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "free-qr@test.com");

        (await Client.SendAsync(Post(owner, "/api/qrcodes", new { Title = "Tisch 1" })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await Client.SendAsync(Post(owner, "/api/qrcodes", new { Title = "Tisch 2" }));
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync()).Should().Be(QrCodeLimitText);

        (await CountAsync(owner)).Should().Be((0, 1));
    }

    [Fact]
    public async Task APaidTenant_HasNoLimit()
    {
        // The seeded tenant already has two menus and two QR codes
        for (var i = 0; i < 3; i++)
        {
            (await Client.PostAsync("/api/menus", Json(NewMenu($"Karte {i}")))).StatusCode.Should().Be(HttpStatusCode.Created);
            (await Client.PostAsync("/api/qrcodes", Json(new { Title = $"Tisch {i}" }))).StatusCode.Should().Be(HttpStatusCode.Created);
        }
        (await CountAsync(CustomWebApplicationFactory.TestUserId)).Should().Be((5, 5));

        var paidOwner = await TestSeedData.SeedOwnerAsync(Factory.Services, "paid@test.com", paid: true);
        for (var i = 0; i < 2; i++)
        {
            (await Client.SendAsync(Post(paidOwner, "/api/menus", NewMenu($"Karte {i}")))).StatusCode.Should().Be(HttpStatusCode.Created);
            (await Client.SendAsync(Post(paidOwner, "/api/qrcodes", new { Title = $"Tisch {i}" }))).StatusCode.Should().Be(HttpStatusCode.Created);
        }
        (await CountAsync(paidOwner)).Should().Be((2, 2));
    }

    [Fact]
    public async Task AFreeTenantAboveTheLimit_KeepsAndEditsItsContent_ButCreatesNothing()
    {
        await TestSeedData.SetPaidAsync(Factory.Services, CustomWebApplicationFactory.TestUserId, paid: false);

        var menu = await Client.PostAsync("/api/menus", Json(NewMenu("Noch eine")));
        menu.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await menu.Content.ReadAsStringAsync()).Should().Be(MenuLimitText);
        var qr = await Client.PostAsync("/api/qrcodes", Json(new { Title = "Noch einer" }));
        qr.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await qr.Content.ReadAsStringAsync()).Should().Be(QrCodeLimitText);

        (await Client.PutAsync($"/api/menus/{TestSeedData.Menu1Id}", Json(NewMenu("Lunch Menu (neu)"))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PutAsync($"/api/qrcodes/{TestSeedData.DirectQrCodeId}",
                Json(new { Title = "Tisch 2 (neu)", MenuId = TestSeedData.Menu2Id })))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.SendAsync(AnonymousGet($"/api/public/menu/{TestSeedData.DirectQrCodeId}")))
            .StatusCode.Should().Be(HttpStatusCode.OK, "guests still get the menu");

        (await CountAsync(CustomWebApplicationFactory.TestUserId)).Should().Be((2, 2), "nothing is deleted or hidden");
    }

    [Fact]
    public async Task AMember_IsLimitedLikeItsOwner_AndPayingCountsAtOnce()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "team-owner@test.com");
        var member = await TestSeedData.SeedMemberAsync(Factory.Services, owner, "team-member@test.com");
        await TestSeedData.SeedContentAsync(Factory.Services, owner, menus: 1, qrCodes: 1);

        (await Client.SendAsync(Post(member, "/api/menus", NewMenu("Vom Manager"))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the owner's tenant is at its limit");

        // The admin toggles the tenant to paid: the next request of the signed-in member may create
        await TestSeedData.SetPaidAsync(Factory.Services, owner, paid: true);
        var created = await Client.SendAsync(Post(member, "/api/menus", NewMenu("Vom Manager")));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        Deserialize<Menu>(await created.Content.ReadAsStringAsync()).ApplicationUserId.Should().Be(Guid.Parse(owner));
        (await Client.SendAsync(Post(member, "/api/qrcodes", new { Title = "Tisch 2" })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // And back: blocked again, the content stays
        await TestSeedData.SetPaidAsync(Factory.Services, owner, paid: false);
        (await Client.SendAsync(Post(owner, "/api/menus", NewMenu("Dritte")))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync(owner)).Should().Be((2, 2));
    }

    [Fact]
    public async Task ThePaidToggleOfTheAdminService_TakesEffectOnTheNextRequest()
    {
        var admin = await TestSeedData.SeedOwnerAsync(Factory.Services, "admin@test.com");
        await TestSeedData.SetGlobalAdminAsync(Factory.Services, admin, admin: true);
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "customer@test.com");
        await TestSeedData.SeedContentAsync(Factory.Services, owner, menus: 1, qrCodes: 0);

        (await Client.SendAsync(Post(owner, "/api/menus", NewMenu("Zweite")))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var scope = Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ITenantAdminService>().SetPaidAsync(owner, true, admin))
                .Succeeded.Should().BeTrue();

        (await Client.SendAsync(Post(owner, "/api/menus", NewMenu("Zweite")))).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
