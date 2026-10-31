using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class MenuCombosControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MenuCombosControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private Task SeedCombosAsync() => TestSeedData.SeedCombosAsync(Factory.Services);

    private static StringContent Json(object dto) =>
        new(JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8, "application/json");

    private static object ValidDto(
        Guid? triggerId = null, List<Guid>? itemIds = null, decimal comboPrice = 13.50m, Guid? menuId = null) => new
    {
        Name = "Lunch Deal",
        Description = "Salad and soup",
        ComboPrice = comboPrice,
        TriggerMenuItemId = triggerId ?? TestSeedData.MenuItem1Id,
        ItemIds = itemIds ?? new List<Guid> { TestSeedData.MenuItem2Id },
        MenuId = menuId ?? TestSeedData.Menu1Id
    };

    // --- GET all ---

    [Fact]
    public async Task GetAll_WithMenuId_ReturnsCombosForThatMenuOnly()
    {
        await SeedCombosAsync();

        var response = await Client.GetAsync($"/api/menucombos?menuId={TestSeedData.Menu1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var combos = await response.Content.ReadFromJsonAsync<List<ComboResponse>>(JsonOptions);
        combos.Should().ContainSingle();
        var combo = combos![0];
        combo.Id.Should().Be(TestSeedData.LunchComboId);
        combo.ComboPrice.Should().Be(12.00m);
        combo.TriggerMenuItem!.Id.Should().Be(TestSeedData.MenuItem1Id);
        combo.Items.Select(i => i.MenuItemId).Should().BeEquivalentTo(new[] { TestSeedData.MenuItem2Id });
    }

    [Fact]
    public async Task GetAll_WithoutMenuId_Returns400()
    {
        var response = await Client.GetAsync("/api/menucombos");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAll_Anonymous_Returns401()
    {
        var response = await Client.SendAsync(AnonymousGet($"/api/menucombos?menuId={TestSeedData.Menu1Id}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- GET by id ---

    [Fact]
    public async Task Get_ExistingCombo_Returns200()
    {
        await SeedCombosAsync();

        var response = await Client.GetAsync($"/api/menucombos/{TestSeedData.LunchComboId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var combo = await response.Content.ReadFromJsonAsync<ComboResponse>(JsonOptions);
        combo!.Name.Should().Be("Salad Combo");
        combo.MenuId.Should().Be(TestSeedData.Menu1Id);
    }

    [Fact]
    public async Task Get_NonExistentCombo_Returns404()
    {
        var response = await Client.GetAsync($"/api/menucombos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- POST ---

    [Fact]
    public async Task Create_ValidCombo_Returns201AndPersists()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var created = await response.Content.ReadFromJsonAsync<ComboResponse>(JsonOptions);
        created!.Name.Should().Be("Lunch Deal");
        created.ComboPrice.Should().Be(13.50m);
        created.TriggerMenuItemId.Should().Be(TestSeedData.MenuItem1Id);
        created.Items.Select(i => i.MenuItemId).Should().BeEquivalentTo(new[] { TestSeedData.MenuItem2Id });

        var get = await Client.GetAsync($"/api/menucombos/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_DuplicateItemIds_AreStoredOnce()
    {
        var dto = ValidDto(itemIds: new List<Guid> { TestSeedData.MenuItem2Id, TestSeedData.MenuItem2Id });

        var response = await Client.PostAsync("/api/menucombos", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ComboResponse>(JsonOptions);
        created!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_ZeroPrice_Returns201()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(comboPrice: 0m)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var dto = new
        {
            Name = "",
            ComboPrice = 10m,
            TriggerMenuItemId = TestSeedData.MenuItem1Id,
            ItemIds = new[] { TestSeedData.MenuItem2Id },
            MenuId = TestSeedData.Menu1Id
        };

        var response = await Client.PostAsync("/api/menucombos", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NameTooLong_Returns400()
    {
        var dto = new
        {
            Name = new string('x', 101),
            ComboPrice = 10m,
            TriggerMenuItemId = TestSeedData.MenuItem1Id,
            ItemIds = new[] { TestSeedData.MenuItem2Id },
            MenuId = TestSeedData.Menu1Id
        };

        var response = await Client.PostAsync("/api/menucombos", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_MissingTriggerItem_Returns400()
    {
        var dto = new
        {
            Name = "No Trigger",
            ComboPrice = 10m,
            ItemIds = new[] { TestSeedData.MenuItem2Id },
            MenuId = TestSeedData.Menu1Id
        };

        var response = await Client.PostAsync("/api/menucombos", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("trigger item is required");
    }

    [Fact]
    public async Task Create_EmptyTriggerItem_Returns400()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(triggerId: Guid.Empty)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("trigger item is required");
    }

    [Fact]
    public async Task Create_NoIncludedItems_Returns400()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(itemIds: new List<Guid>())));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("at least one included item");
    }

    [Fact]
    public async Task Create_NullIncludedItems_Returns400()
    {
        var dto = new
        {
            Name = "No Items",
            ComboPrice = 10m,
            TriggerMenuItemId = TestSeedData.MenuItem1Id,
            MenuId = TestSeedData.Menu1Id
        };

        var response = await Client.PostAsync("/api/menucombos", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("at least one included item");
    }

    [Fact]
    public async Task Create_NegativePrice_Returns400()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(comboPrice: -0.01m)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ComboPrice must not be negative");
    }

    [Fact]
    public async Task Create_NonExistentMenu_Returns400()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(menuId: Guid.NewGuid())));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_TriggerItemFromOtherMenu_Returns400()
    {
        // MenuItem3 belongs to Menu2
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(triggerId: TestSeedData.MenuItem3Id)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("trigger item does not belong");
    }

    [Fact]
    public async Task Create_IncludedItemFromOtherMenu_Returns400()
    {
        var dto = ValidDto(itemIds: new List<Guid> { TestSeedData.MenuItem2Id, TestSeedData.MenuItem3Id });

        var response = await Client.PostAsync("/api/menucombos", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("must belong to the combo's menu");
    }

    [Fact]
    public async Task Create_NonExistentIncludedItem_Returns400()
    {
        var response = await Client.PostAsync("/api/menucombos", Json(ValidDto(itemIds: new List<Guid> { Guid.NewGuid() })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- PUT ---

    [Fact]
    public async Task Update_ExistingCombo_Returns200AndReplacesItems()
    {
        await SeedCombosAsync();
        var dto = new
        {
            Name = "Salad Combo XL",
            Description = "Updated",
            ComboPrice = 14.00m,
            TriggerMenuItemId = TestSeedData.MenuItem2Id,
            ItemIds = new[] { TestSeedData.MenuItem1Id },
            MenuId = TestSeedData.Menu1Id
        };

        var response = await Client.PutAsync($"/api/menucombos/{TestSeedData.LunchComboId}", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ComboResponse>(JsonOptions);
        updated!.Name.Should().Be("Salad Combo XL");
        updated.ComboPrice.Should().Be(14.00m);
        updated.TriggerMenuItemId.Should().Be(TestSeedData.MenuItem2Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var itemIds = await db.MenuComboItems
            .Where(i => i.MenuComboId == TestSeedData.LunchComboId)
            .Select(i => i.MenuItemId)
            .ToListAsync();
        itemIds.Should().BeEquivalentTo(new[] { TestSeedData.MenuItem1Id });
    }

    [Fact]
    public async Task Update_NonExistentCombo_Returns404()
    {
        var response = await Client.PutAsync($"/api/menucombos/{Guid.NewGuid()}", Json(ValidDto()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ItemFromOtherMenu_Returns400()
    {
        await SeedCombosAsync();
        var dto = ValidDto(itemIds: new List<Guid> { TestSeedData.MenuItem3Id });

        var response = await Client.PutAsync($"/api/menucombos/{TestSeedData.LunchComboId}", Json(dto));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NegativePrice_Returns400()
    {
        await SeedCombosAsync();

        var response = await Client.PutAsync($"/api/menucombos/{TestSeedData.LunchComboId}", Json(ValidDto(comboPrice: -1m)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- DELETE ---

    [Fact]
    public async Task Delete_ExistingCombo_Returns204AndRemovesItems()
    {
        await SeedCombosAsync();

        var response = await Client.DeleteAsync($"/api/menucombos/{TestSeedData.LunchComboId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var get = await Client.GetAsync($"/api/menucombos/{TestSeedData.LunchComboId}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.MenuComboItems.AnyAsync(i => i.MenuComboId == TestSeedData.LunchComboId)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_NonExistentCombo_Returns404()
    {
        var response = await Client.DeleteAsync($"/api/menucombos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private record ComboResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal ComboPrice { get; init; }
        public Guid MenuId { get; init; }
        public Guid TriggerMenuItemId { get; init; }
        public MenuItemResponse? TriggerMenuItem { get; init; }
        public List<ComboItemResponse> Items { get; init; } = new();
    }

    private record ComboItemResponse
    {
        public Guid Id { get; init; }
        public Guid MenuItemId { get; init; }
    }

    private record MenuItemResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal Price { get; init; }
    }
}
