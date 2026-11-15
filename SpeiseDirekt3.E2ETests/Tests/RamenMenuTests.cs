using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>The ramen menu card from <see cref="E2ESeedData.RamenDishes"/> as guests see it on <c>/menu/{id}</c>.</summary>
public class RamenMenuTests : E2ETestBase
{
    public RamenMenuTests(E2EFixture fixture) : base(fixture) { }

    /// <summary>The entry of a dish (exact name: "Smoky Men" vs. "Veggie Smoky Men"). No photos → list rows.</summary>
    private ILocator DishCard(string name) => ItemEntry(name);

    /// <summary>Allergen codes as the app renders them: sorted by code, comma separated.</summary>
    private static string ExpectedCodes(E2ESeedData.RamenDish dish) =>
        string.Join(", ", dish.AllergenCodes.OrderBy(c => c, StringComparer.Ordinal));

    [Fact]
    public async Task RamenMenu_ShowsAllDishesWithPriceAndAllergenCodes()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync(E2ESeedData.RamenMenuName);
        // No dish has a photo: the category renders as a compact list
        await Assertions.Expect(Page.GetByTestId("list-row")).ToHaveCountAsync(E2ESeedData.RamenDishes.Count);
        await Assertions.Expect(Page.GetByTestId("item-card")).ToHaveCountAsync(0);

        foreach (var dish in E2ESeedData.RamenDishes)
        {
            var card = DishCard(dish.Name);
            await Assertions.Expect(card).ToHaveCountAsync(1);
            await Assertions.Expect(card.GetByTestId("item-price")).ToHaveTextAsync("€ 17,40");
            await Assertions.Expect(card).ToContainTextAsync(ExpectedCodes(dish));
        }
    }

    [Theory]
    [InlineData("Tokyo Ramen")]
    [InlineData("Hokkaido Ramen")]
    [InlineData("Veggie Smoky Men")]
    [InlineData("Yasai Ramen")]
    public async Task RamenDish_SheetShowsDescriptionAndAllergens(string name)
    {
        var dish = E2ESeedData.RamenDishes.Single(d => d.Name == name);
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await ClickMenuItemAsync(name);

        await Assertions.Expect(ItemSheet).ToBeVisibleAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(ItemSheet).ToContainTextAsync(dish.Description);
        await Assertions.Expect(ItemSheet).ToContainTextAsync(ExpectedCodes(dish));
        await Assertions.Expect(ItemSheet.GetByTestId("item-price")).ToHaveTextAsync("€ 17,40");
    }
}
