using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Guest-facing UI details: dietary badges (vegan/vegetarisch/scharf), allergen legend in the item
/// overlay, the editor preview with allergens, and the unified notice pages.
/// </summary>
public class GuestUiTests : BackOfficeTestBase
{
    public GuestUiTests(E2EFixture fixture) : base(fixture) { }

    private static Regex Exact(string text) => new($"^\\s*{Regex.Escape(text)}\\s*$");

    private ILocator DishCard(IPage page, string name) =>
        page.Locator("div.cursor-pointer", new() { Has = page.Locator("h3", new() { HasTextRegex = Exact(name) }) });

    // --- Dietary badges (ramen card from the seed data) ---

    [Fact]
    public async Task RamenCard_ShowsDietaryBadgesLikeThePrintedMenu()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        foreach (var dish in E2ESeedData.RamenDishes)
        {
            var card = DishCard(Page, dish.Name);
            await Assertions.Expect(card.GetByTestId("badge-vegan")).ToHaveCountAsync(dish.Vegan ? 1 : 0);
            // "vegan" implies vegetarian: only one of the two badges is shown
            await Assertions.Expect(card.GetByTestId("badge-vegetarian")).ToHaveCountAsync(dish.Vegetarian && !dish.Vegan ? 1 : 0);
            await Assertions.Expect(card.GetByTestId("badge-spicy")).ToHaveCountAsync(dish.Spicy ? 1 : 0);
        }
    }

    [Fact]
    public async Task ItemOverlay_ShowsDietaryBadges()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await DishCard(Page, "Yasai Ramen").ClickAsync();

        await Assertions.Expect(Overlay.GetByTestId("badge-vegan")).ToHaveTextAsync(new Regex("vegan"));
        await Assertions.Expect(Overlay.GetByTestId("badge-spicy")).ToHaveTextAsync(new Regex("scharf"));
        await Assertions.Expect(Overlay.GetByTestId("badge-vegetarian")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DishWithoutLabels_ShowsNoBadges()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await Assertions.Expect(DishCard(Page, "Tokyo Ramen").GetByTestId("dietary-badges")).ToHaveCountAsync(0);
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
        await OpenPublicMenuPageAsync(guest, $"/menu/{menuId}");

        var tofu = DishCard(guest, "Chili Tofu Bowl");
        await Assertions.Expect(tofu.GetByTestId("badge-vegan")).ToBeVisibleAsync();
        await Assertions.Expect(tofu.GetByTestId("badge-spicy")).ToBeVisibleAsync();
        var cheese = DishCard(guest, "Käse Bowl");
        await Assertions.Expect(cheese.GetByTestId("badge-vegetarian")).ToBeVisibleAsync();
        await Assertions.Expect(cheese.GetByTestId("badge-spicy")).ToHaveCountAsync(0);
    }

    // --- Allergen legend ---

    [Fact]
    public async Task ItemOverlay_ShowsAllergenLegendWithNames()
    {
        var dish = E2ESeedData.RamenDishes.Single(d => d.Name == "Hokkaido Ramen");
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await DishCard(Page, dish.Name).ClickAsync();

        var legend = Overlay.GetByTestId("allergen-legend");
        await Assertions.Expect(legend).ToBeVisibleAsync();
        await Assertions.Expect(legend.Locator("li")).ToHaveCountAsync(dish.AllergenCodes.Length);
        foreach (var code in dish.AllergenCodes)
            await Assertions.Expect(legend).ToContainTextAsync(E2ESeedData.RamenAllergens[code]);
        // Sorted by code
        await Assertions.Expect(legend.Locator("li").First).ToContainTextAsync("Glutenhaltiges Getreide");
        await Assertions.Expect(legend.Locator("li").Last).ToContainTextAsync("Sesam");
    }

    [Fact]
    public async Task ItemOverlay_WithoutAllergens_ShowsNoLegend()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        await ClickMenuItemAsync("Gartensalat");

        await Assertions.Expect(Overlay).ToBeVisibleAsync();
        await Assertions.Expect(Overlay.GetByTestId("allergen-legend")).ToHaveCountAsync(0);
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

        var card = DishCard(Page, "Gemüsesuppe");
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("span", new() { HasTextRegex = Exact("L") })).ToBeVisibleAsync();

        // Saving the menu afterwards still works (allergens are now part of the loaded graph)
        await Page.Locator("#menuDesc").FillAsync("Neue Beschreibung");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menuId}$"));
        await Assertions.Expect(Page.GetByText("Neue Beschreibung").First).ToBeVisibleAsync();
    }

    // --- Notice pages ---

    [Fact]
    public async Task UnknownMenuPreview_ShowsNotFoundNotice()
    {
        await Page.GotoAsync($"/menu/preview/{Guid.NewGuid()}");

        var notice = Page.GetByTestId("guest-notice");
        await Assertions.Expect(notice).ToHaveAttributeAsync("data-kind", "notfound");
        await Assertions.Expect(notice.GetByRole(AriaRole.Heading, new() { Name = "Menü nicht gefunden" })).ToBeVisibleAsync();
    }
}
