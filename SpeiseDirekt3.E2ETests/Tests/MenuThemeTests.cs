using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Design themes (<c>Menu.Theme</c>) style the public menu display via <c>css/menu-themes.css</c>.
/// The stylesheet is local, so its colors and fonts are checked even while the Tailwind CDN is blocked.
/// </summary>
public class MenuThemeTests : BackOfficeTestBase
{
    public MenuThemeTests(E2EFixture fixture) : base(fixture) { }

    private ILocator MenuDisplay(IPage page) => page.GetByTestId("menu-display");

    private async Task<Guid> SeedMenuAsync(DesignTheme theme)
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId };
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = $"Theme {theme}", Description = "Theme-Test", Theme = theme,
                Language = MenuLanguage.German, Categories = new List<Category> { category }
            });
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(), Name = "Testgericht", Description = "Beschreibung", Price = 9.90m, CategoryId = category.Id
            });
            return Task.CompletedTask;
        });
        return menuId;
    }

    private static Task<string> ComputedAsync(ILocator locator, string property) =>
        locator.EvaluateAsync<string>($"el => getComputedStyle(el).{property}");

    [Theory]
    [InlineData(DesignTheme.Elegant, "rgb(252, 248, 244)", "Playfair Display")]
    [InlineData(DesignTheme.Classic, "rgb(251, 245, 242)", "Playfair Display")]
    [InlineData(DesignTheme.Minimal, "rgb(255, 255, 255)", "Inter")]
    [InlineData(DesignTheme.Fancy, "rgb(255, 253, 252)", "Pacifico")]
    [InlineData(DesignTheme.Dark, "rgb(18, 18, 18)", "Playfair Display")]
    public async Task Theme_ChangesColorsAndFontsOfTheGuestView(DesignTheme theme, string pageBackground, string headingFont)
    {
        var menuId = await SeedMenuAsync(theme);

        await OpenMenuAsync(menuId);

        var display = MenuDisplay(Page);
        await Assertions.Expect(display).ToHaveAttributeAsync("data-theme", theme.ToString().ToLowerInvariant());
        (await ComputedAsync(display, "backgroundColor")).Should().Be(pageBackground);
        (await ComputedAsync(display.Locator(".sd-header h1"), "fontFamily")).Should().Contain(headingFont);
    }

    [Fact]
    public async Task DarkTheme_CardsAreDark_DespiteBootstrapBgWhiteImportant()
    {
        // Bootstrap's .bg-white is !important; the theme must still win, otherwise light text sits on white cards
        var menuId = await SeedMenuAsync(DesignTheme.Dark);
        await OpenMenuAsync(menuId);

        var card = MenuDisplay(Page).Locator("div.cursor-pointer").First;
        (await ComputedAsync(card, "backgroundColor")).Should().Be("rgb(30, 30, 30)");
        (await ComputedAsync(card.Locator("h3"), "color")).Should().Be("rgb(232, 232, 232)");
        // Header title uses the theme's header color (Bootstrap's .text-white is !important too)
        (await ComputedAsync(MenuDisplay(Page).Locator(".sd-header h1"), "color")).Should().Be("rgb(242, 212, 218)");
    }

    [Fact]
    public async Task ModernTheme_IsTheDefaultLook_WithoutThemeOverrides()
    {
        var menuId = await SeedMenuAsync(DesignTheme.Modern);
        await OpenMenuAsync(menuId);

        var display = MenuDisplay(Page);
        await Assertions.Expect(display).ToHaveAttributeAsync("data-theme", "modern");
        // No theme rule applies: transparent while the CDN is blocked, Tailwind's bg-gray-50 with real Tailwind
        (await ComputedAsync(display, "backgroundColor")).Should().BeOneOf("rgba(0, 0, 0, 0)", "rgb(249, 250, 251)");
    }

    [Fact]
    public async Task HeaderShade_IsTranslucent_NotSolidBlack()
    {
        // "bg-opacity-20" does not exist in Bootstrap, whose !important .bg-black made the header shade solid black
        var menuId = await SeedMenuAsync(DesignTheme.Modern);
        await OpenMenuAsync(menuId);

        var shade = MenuDisplay(Page).Locator(".sd-header-shade");
        (await ComputedAsync(shade, "backgroundColor")).Should().NotBe("rgb(0, 0, 0)");
    }

    [Fact]
    public async Task ThemeChosenInEditor_UpdatesPreview_AndIsShownToGuests()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Theme Bistro");
        await CreateCategoryViaUiAsync(menuId, "Pizza");
        await CreateMenuItemViaUiAsync(menuId, "Margherita", "Tomate, Mozzarella", 9.00m);

        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        await Assertions.Expect(MenuDisplay(Page)).ToHaveAttributeAsync("data-theme", "modern");

        // The live preview next to the form follows the selection
        await Page.Locator("#themeSelect").SelectOptionAsync("Dark");
        await Assertions.Expect(MenuDisplay(Page)).ToHaveAttributeAsync("data-theme", "dark");

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menuId}$"));

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenPublicMenuPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(MenuDisplay(guest)).ToHaveAttributeAsync("data-theme", "dark");
        (await ComputedAsync(MenuDisplay(guest), "backgroundColor")).Should().Be("rgb(18, 18, 18)");
    }
}
