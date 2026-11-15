using FluentAssertions;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Guest-facing UI details: label chips (vegan/vegetarisch/scharf), allergen legend in the item sheet,
/// the editor preview with allergens, prices, and the unified notice pages.
/// </summary>
public class GuestUiTests : BackOfficeTestBase
{
    public GuestUiTests(E2EFixture fixture) : base(fixture) { }

    private static ILocator DishCard(IPage page, string name) => ItemEntry(page, name);

    // --- Label chips (ramen card from the seed data) ---

    [Fact]
    public async Task RamenCard_ShowsLabelChipsLikeThePrintedMenu()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        foreach (var dish in E2ESeedData.RamenDishes)
        {
            var card = DishCard(Page, dish.Name);
            await Assertions.Expect(card.GetByTestId("label-vegan")).ToHaveCountAsync(dish.Vegan ? 1 : 0);
            // "vegan" implies vegetarian: only one of the two labels is shown
            await Assertions.Expect(card.GetByTestId("label-vegetarian")).ToHaveCountAsync(dish.Vegetarian && !dish.Vegan ? 1 : 0);
            await Assertions.Expect(card.GetByTestId("label-spicy")).ToHaveCountAsync(dish.Spicy ? 1 : 0);
        }
    }

    [Fact]
    public async Task ItemSheet_ShowsLabelChipsInFixedOrder()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await ClickMenuItemAsync("Yasai Ramen");

        await Assertions.Expect(ItemSheet.GetByTestId("label-vegan")).ToHaveTextAsync("Vegan");
        await Assertions.Expect(ItemSheet.GetByTestId("label-spicy")).ToHaveTextAsync("Scharf");
        await Assertions.Expect(ItemSheet.GetByTestId("label-vegetarian")).ToHaveCountAsync(0);
        // One helper decides the order: vegan/vegetarisch first, then scharf
        await Assertions.Expect(ItemSheet.GetByTestId("item-labels").Locator("li")).ToHaveTextAsync(new[] { "Vegan", "Scharf" });
        // The card carries the label keys for scripts (filters)
        await Assertions.Expect(DishCard(Page, "Yasai Ramen")).ToHaveAttributeAsync("data-labels", "vegan spicy");
    }

    [Fact]
    public async Task DishWithoutLabels_ShowsNoLabelChips()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await Assertions.Expect(DishCard(Page, "Tokyo Ramen").GetByTestId("item-labels")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LabelsSetInItemEditor_AreShownToGuests()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Label Menü");
        await CreateCategoryViaUiAsync(menuId, "Bowls");
        await CreateMenuItemViaUiAsync(menuId, "Chili Tofu Bowl", "Mit Reis", 11.00m, new DietaryLabels(Vegan: true, Spicy: true));
        await CreateMenuItemViaUiAsync(menuId, "Käse Bowl", "Mit Ei", 10.00m, new DietaryLabels(Vegetarian: true));

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");

        var tofu = DishCard(guest, "Chili Tofu Bowl");
        await Assertions.Expect(tofu.GetByTestId("label-vegan")).ToBeVisibleAsync();
        await Assertions.Expect(tofu.GetByTestId("label-spicy")).ToBeVisibleAsync();
        var cheese = DishCard(guest, "Käse Bowl");
        await Assertions.Expect(cheese.GetByTestId("label-vegetarian")).ToBeVisibleAsync();
        await Assertions.Expect(cheese.GetByTestId("label-spicy")).ToHaveCountAsync(0);
    }

    // --- Allergen legend ---

    [Fact]
    public async Task ItemSheet_ShowsAllergenLegendWithNames()
    {
        var dish = E2ESeedData.RamenDishes.Single(d => d.Name == "Hokkaido Ramen");
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await ClickMenuItemAsync(dish.Name);

        var legend = ItemSheet.GetByTestId("allergen-legend");
        await Assertions.Expect(legend).ToBeVisibleAsync();
        await Assertions.Expect(legend.Locator("li")).ToHaveCountAsync(dish.AllergenCodes.Length);
        foreach (var code in dish.AllergenCodes)
            await Assertions.Expect(legend).ToContainTextAsync(E2ESeedData.RamenAllergens[code]);
        // Sorted by code
        await Assertions.Expect(legend.Locator("li").First).ToContainTextAsync("Glutenhaltiges Getreide");
        await Assertions.Expect(legend.Locator("li").Last).ToContainTextAsync("Sesam");
    }

    [Fact]
    public async Task ItemSheet_WithoutAllergens_ShowsNoLegend()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        await ClickMenuItemAsync("Gartensalat");

        await Assertions.Expect(ItemSheet).ToBeVisibleAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("allergen-legend")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task MenuEditorPreview_ShowsAllergenCodes()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Vorschau Menü");
        await CreateCategoryViaUiAsync(menuId, "Suppen");
        await CreateAllergenViaUiAsync(menuId, "L", "Sellerie");
        await CreateMenuItemViaUiAsync(menuId, "Gemüsesuppe", "Hausgemacht", 5.90m, "L");

        await GotoInteractiveAsync($"/menu/edit/{menuId}");

        // The phone frame shows the real guest page in an iframe
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        var row = frame.Locator("[data-testid='list-row']", new() { Has = frame.Locator("h3", new() { HasTextRegex = ExactText("Gemüsesuppe") }) });
        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.GetByTestId("item-allergens")).ToHaveTextAsync("L");

        // Saving the menu afterwards still works
        await Page.Locator("#menuDesc").FillAsync("Neue Beschreibung");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menuId}$"));
        await Assertions.Expect(Page.GetByText("Neue Beschreibung").First).ToBeVisibleAsync();
    }

    // --- Layout / assets ---

    [Fact]
    public async Task MenuCard_PriceStaysOnOneLine_OnNarrowPhones()
    {
        // Tokyo Ramen has six allergen codes; on narrow screens "€" and the amount used to wrap
        await Page.SetViewportSizeAsync(360, 780);
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        var price = DishCard(Page, "Tokyo Ramen").GetByTestId("item-price");
        await Assertions.Expect(price).ToHaveTextAsync("€ 17,40");
        await Assertions.Expect(price).ToHaveCSSAsync("white-space", "nowrap");
        var box = await price.BoundingBoxAsync();
        box!.Height.Should().BeLessThan(30, "the price must not wrap onto a second line");
    }

    [Fact]
    public async Task BootstrapJs_IsServedLocally_PopoversWork()
    {
        var bootstrapRequests = new List<string>();
        Page.Request += (_, r) => { if (r.Url.Contains("bootstrap") && r.Url.EndsWith(".js")) bootstrapRequests.Add(r.Url); };

        await RegisterNewOwnerAsync();
        await GotoInteractiveAsync("/menu/edit"); // uses InfoPopoverComp → initPopover → bootstrap.Popover

        (await Page.EvaluateAsync<bool>("typeof window.bootstrap?.Popover === 'function'")).Should().BeTrue();
        bootstrapRequests.Should().NotBeEmpty()
            .And.OnlyContain(url => url.StartsWith(App.ServerAddress), "bootstrap.bundle.js must not come from a CDN");
        // The circuit survived initPopover (no "unhandled error" bar) and the form is usable
        await Page.Locator("#menuName").FillAsync("Popover Test");
        await Assertions.Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    // --- Notice pages ---

    [Fact]
    public async Task UnknownMenuPreview_ShowsNotFoundNotice()
    {
        await RegisterNewOwnerAsync(); // the preview requires a login
        await Page.GotoAsync($"/menu/preview/{Guid.NewGuid()}");

        var notice = Page.GetByTestId("guest-notice");
        await Assertions.Expect(notice).ToHaveAttributeAsync("data-kind", "notfound");
        await Assertions.Expect(notice.GetByRole(AriaRole.Heading, new() { Name = "Menü nicht gefunden" })).ToBeVisibleAsync();
    }
}
