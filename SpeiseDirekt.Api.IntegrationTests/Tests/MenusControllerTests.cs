using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

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
    public async Task Create_WithoutDescription_Returns201_WithEmptyDescription()
    {
        // The description is optional (AP13): omitted or null is stored as ""
        var content = new StringContent(
            JsonSerializer.Serialize(new { Name = "Late Night" }, JsonOptions), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/menus", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Menu>(JsonOptions);
        created!.Name.Should().Be("Late Night");
        created.Description.Should().BeEmpty();
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

    // --- Language versions (translationGroupId) ---------------------------------------------------------------

    /// <summary>Links the seeded Lunch Menu (German) and Dinner Menu (English) as language versions.</summary>
    private async Task<Guid> LinkSeedMenusAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMenuTranslationService>()
            .LinkAsync(TestSeedData.Menu1Id, TestSeedData.Menu2Id);
        result.Succeeded.Should().BeTrue(result.Error);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Menus.AsNoTracking().SingleAsync(m => m.Id == TestSeedData.Menu1Id)).TranslationGroupId!.Value;
    }

    [Fact]
    public async Task GetById_UnlinkedMenu_HasNoTranslationGroup()
    {
        var json = await Client.GetStringAsync($"/api/menus/{TestSeedData.Menu1Id}");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("translationGroupId", out var group).Should().BeTrue();
        group.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetById_LinkedMenus_ShareTheTranslationGroupId()
    {
        var groupId = await LinkSeedMenusAsync();

        var lunch = await Client.GetFromJsonAsync<Menu>($"/api/menus/{TestSeedData.Menu1Id}", JsonOptions);
        var dinner = await Client.GetFromJsonAsync<Menu>($"/api/menus/{TestSeedData.Menu2Id}", JsonOptions);
        var all = await Client.GetFromJsonAsync<List<Menu>>("/api/menus", JsonOptions);

        lunch!.TranslationGroupId.Should().Be(groupId);
        dinner!.TranslationGroupId.Should().Be(groupId);
        all!.Where(m => m.TranslationGroupId == groupId).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLanguageVersions_ListsEveryVersionInLanguageOrder()
    {
        await LinkSeedMenusAsync();

        var response = await Client.GetAsync($"/api/menus/{TestSeedData.Menu2Id}/language-versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await response.Content.ReadFromJsonAsync<List<MenuLanguageVersionDto>>(JsonOptions);
        versions.Should().Equal(
            new MenuLanguageVersionDto(TestSeedData.Menu1Id, "Lunch Menu", MenuLanguage.German, false),
            new MenuLanguageVersionDto(TestSeedData.Menu2Id, "Dinner Menu", MenuLanguage.English, true));
    }

    [Fact]
    public async Task GetLanguageVersions_UnlinkedMenu_ReturnsJustTheMenu_UnknownMenu404()
    {
        var versions = await Client.GetFromJsonAsync<List<MenuLanguageVersionDto>>(
            $"/api/menus/{TestSeedData.Menu1Id}/language-versions", JsonOptions);
        versions.Should().Equal(new MenuLanguageVersionDto(TestSeedData.Menu1Id, "Lunch Menu", MenuLanguage.German, true));

        var unknown = await Client.GetAsync($"/api/menus/{Guid.NewGuid()}/language-versions");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_TranslationGroupIdInBody_IsIgnored()
    {
        var groupId = await LinkSeedMenusAsync();
        var body = new
        {
            name = "Lunch Menu", description = "Daily lunch specials", theme = DesignTheme.Modern,
            language = MenuLanguage.German, translationGroupId = Guid.NewGuid()
        };

        var response = await Client.PutAsJsonAsync($"/api/menus/{TestSeedData.Menu1Id}", body, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var menu = await Client.GetFromJsonAsync<Menu>($"/api/menus/{TestSeedData.Menu1Id}", JsonOptions);
        menu!.TranslationGroupId.Should().Be(groupId);
    }

    [Fact]
    public async Task Update_LanguageOfAnotherVersion_Returns400_AndKeepsTheMenu()
    {
        await LinkSeedMenusAsync();
        var dto = new MenuDto { Name = "Lunch Menu", Description = "Daily lunch specials", Language = MenuLanguage.English };

        var response = await Client.PutAsJsonAsync($"/api/menus/{TestSeedData.Menu1Id}", dto, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Dinner Menu");
        var menu = await Client.GetFromJsonAsync<Menu>($"/api/menus/{TestSeedData.Menu1Id}", JsonOptions);
        menu!.Language.Should().Be(MenuLanguage.German);

        // A language no other version has is fine
        var french = dto with { Language = MenuLanguage.French };
        (await Client.PutAsJsonAsync($"/api/menus/{TestSeedData.Menu1Id}", french, JsonOptions)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_OneOfTwoVersions_UnlinksTheOther()
    {
        await LinkSeedMenusAsync();

        var response = await Client.DeleteAsync($"/api/menus/{TestSeedData.Menu2Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var lunch = await Client.GetFromJsonAsync<Menu>($"/api/menus/{TestSeedData.Menu1Id}", JsonOptions);
        lunch!.TranslationGroupId.Should().BeNull();
        lunch.Name.Should().Be("Lunch Menu");
    }
}
