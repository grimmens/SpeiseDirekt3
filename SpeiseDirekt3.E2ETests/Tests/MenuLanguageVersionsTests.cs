using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP5, back office: the "Language versions" card of the menu settings — "Add translation" (a complete, independent
/// copy in the chosen language that opens in the editor), "Link existing menu", "Unlink", one menu per language in a
/// group, and deleting one version (the others stay).
/// </summary>
public class MenuLanguageVersionsTests : BackOfficeTestBase
{
    public MenuLanguageVersionsTests(E2EFixture fixture) : base(fixture) { }

    private ILocator Card => Page.GetByTestId("language-versions");

    private async Task<Guid> RegisterOwnerAsync() => await OwnerIdAsync(await RegisterNewOwnerAsync());

    private async Task<Menu> LoadMenuAsync(Guid menuId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.Menus.IgnoreQueryFilters().AsNoTracking()
            .Include(m => m.Categories!).ThenInclude(c => c.MenuItems!).ThenInclude(i => i.PriceOptions)
            .Include(m => m.Categories!).ThenInclude(c => c.MenuItems!).ThenInclude(i => i.Allergens)
            .Include(m => m.Allergens)
            .AsSplitQuery()
            .SingleAsync(m => m.Id == menuId);
    }

    private async Task<Guid?> GroupOfAsync(Guid menuId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.Menus.IgnoreQueryFilters().Where(m => m.Id == menuId).Select(m => m.TranslationGroupId).SingleAsync();
    }

    private static Guid IdFromUrl(string url) =>
        Guid.Parse(Regex.Match(url, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").Value);

    [Fact]
    public async Task AddTranslation_CreatesACompleteIndependentCopy_AndOpensIt()
    {
        var owner = await RegisterOwnerAsync();
        var german = await GuestDemoData.SeedAsync(App, owner, DesignTheme.Classic);
        // An allergen named by the owner (not the EU standard text) keeps its name in the copy
        await App.SeedAsync(owner, async db =>
        {
            var celery = await db.Allergens.SingleAsync(a => a.MenuId == german.MenuId && a.Code == "L");
            celery.Name = "Sellerie (Knolle)";
        });

        await GotoInteractiveAsync($"/menu/edit/{german.MenuId}");
        await Assertions.Expect(Card.GetByTestId("language-versions-empty")).ToBeVisibleAsync();
        await Card.GetByRole(AriaRole.Button, new() { Name = "Add translation" }).ClickAsync();
        await Assertions.Expect(Card.Locator("#translationLanguage")).ToHaveValueAsync("English");
        await Card.GetByRole(AriaRole.Button, new() { Name = "Create copy" }).ClickAsync();

        // The copy's settings open, linked with the original
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menu/edit/(?!" + german.MenuId + ")[0-9a-f-]{36}$"));
        var copyId = IdFromUrl(Page.Url);
        await Assertions.Expect(Page.Locator("#menuName")).ToHaveValueAsync($"{GuestDemoData.MenuName} (English)");
        await Assertions.Expect(Page.Locator("#languageSelect")).ToHaveValueAsync("English");
        await Assertions.Expect(Card.GetByTestId("language-version")).ToHaveCountAsync(2);
        await Assertions.Expect(Card.GetByTestId("language-version").Nth(0)).ToContainTextAsync(GuestDemoData.MenuName);
        await Assertions.Expect(Card.GetByTestId("language-version").Nth(1)).ToContainTextAsync("this menu");

        var original = await LoadMenuAsync(german.MenuId);
        var copy = await LoadMenuAsync(copyId);
        copy.Language.Should().Be(MenuLanguage.English);
        copy.TranslationGroupId.Should().NotBeNull().And.Be(original.TranslationGroupId);
        copy.Description.Should().Be(original.Description);
        copy.Theme.Should().Be(DesignTheme.Classic);
        copy.ApplicationUserId.Should().Be(owner);
        copy.Categories!.Select(c => (c.Name, c.Description, c.SortOrder))
            .Should().BeEquivalentTo(original.Categories!.Select(c => (c.Name, c.Description, c.SortOrder)));
        var originalItems = original.Categories!.SelectMany(c => c.MenuItems!).ToList();
        var copiedItems = copy.Categories!.SelectMany(c => c.MenuItems!).ToList();
        copiedItems.Select(i => (i.Name, i.Description, i.Price, i.SortOrder, i.ImagePath, i.IsVegan, i.IsVegetarian, i.IsSpicy, i.IsHalal, i.IsAlcoholFree, i.IsNew, i.IsPopular))
            .Should().BeEquivalentTo(originalItems.Select(i => (i.Name, i.Description, i.Price, i.SortOrder, i.ImagePath, i.IsVegan, i.IsVegetarian, i.IsSpicy, i.IsHalal, i.IsAlcoholFree, i.IsNew, i.IsPopular)));
        copiedItems.SelectMany(i => i.PriceOptions.Select(o => (i.Name, o.Kind, o.Name, o.Price, o.SortOrder)))
            .Should().BeEquivalentTo(originalItems.SelectMany(i => i.PriceOptions.Select(o => (i.Name, o.Kind, o.Name, o.Price, o.SortOrder))));
        copiedItems.Select(i => (i.Name, Codes: string.Join(",", i.Allergens.Select(a => a.Code).Order())))
            .Should().BeEquivalentTo(originalItems.Select(i => (i.Name, Codes: string.Join(",", i.Allergens.Select(a => a.Code).Order()))));
        copiedItems.SelectMany(i => i.Allergens).Should().OnlyContain(a => a.MenuId == copyId, "the copy's items point to the copy's allergens");
        copiedItems.Select(i => i.Id).Should().NotIntersectWith(originalItems.Select(i => i.Id));
        copy.Allergens!.Select(a => (a.Code, a.Name)).Should().BeEquivalentTo(new[]
        {
            ("A", "Cereals containing gluten"), ("C", "Eggs"), ("D", "Fish"), ("G", "Milk"), ("L", "Sellerie (Knolle)"), ("O", "Sulphites")
        });
        await using (var db = App.CreateDbContext(Guid.Empty))
        {
            var combo = await db.MenuCombos.IgnoreQueryFilters().Include(c => c.Items).SingleAsync(c => c.MenuId == copyId);
            combo.Name.Should().Be(GuestDemoData.ComboName);
            copiedItems.Single(i => i.Id == combo.TriggerMenuItemId).Name.Should().Be(GuestDemoData.DishWithExtras);
            combo.Items.Select(ci => copiedItems.Single(i => i.Id == ci.MenuItemId).Name).Should().BeEquivalentTo("Tiramisu", "Mineralwasser 0,5 l");
            (await db.QRCodes.IgnoreQueryFilters().CountAsync(q => q.MenuId == copyId)).Should().Be(0);
        }

        // The preview of the copy shows the English UI and the language button
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        await Assertions.Expect(frame.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(frame.GetByTestId("language-button")).ToHaveTextAsync("EN");

        // Editing the copy leaves the original alone
        await Page.Locator("#menuName").FillAsync("Trattoria Lago – English menu");
        await Page.Locator("#menuDesc").FillAsync("Italian cooking by the lake");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{copyId}$"));
        (await LoadMenuAsync(german.MenuId)).Name.Should().Be(GuestDemoData.MenuName);
        (await GroupOfAsync(copyId)).Should().Be(original.TranslationGroupId, "saving the settings keeps the link");

        // Deleting the copy leaves the original complete, photos included
        await GotoInteractiveAsync("/menus");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete menu Trattoria Lago – English menu" }).ClickAsync();
        var dialog = Page.Locator("#confirmationModal");
        await Assertions.Expect(dialog).ToContainTextAsync("Its other language versions (German) stay.");
        await dialog.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(MenuCardLink(Page, "Trattoria Lago – English menu")).ToHaveCountAsync(0);

        var after = await LoadMenuAsync(german.MenuId);
        after.TranslationGroupId.Should().BeNull("a group of one is dissolved");
        after.Categories!.SelectMany(c => c.MenuItems!).Should().HaveCount(originalItems.Count);
        after.Allergens.Should().HaveCount(original.Allergens!.Count);
        var photo = originalItems.First(i => i.ImagePath is not null).ImagePath!;
        var response = await Page.APIRequest.GetAsync("/" + photo);
        response.Status.Should().Be(200, "the photos are shared read-only and survive the copy's deletion");
    }

    [Fact]
    public async Task LinkExistingMenu_AndUnlink()
    {
        var owner = await RegisterOwnerAsync();
        var german = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.German, "Mittagskarte", "Schnitzel", 14.90m);
        var english = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.English, "Lunch menu", "Schnitzel", 14.90m);
        await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.German, "Abendkarte", "Tafelspitz", 22.50m);

        await GotoInteractiveAsync($"/menu/edit/{german}");
        await Card.GetByRole(AriaRole.Button, new() { Name = "Link existing menu" }).ClickAsync();
        var select = Card.Locator("#linkMenu");
        // Only menus in a language the group does not have: not the other German menu
        await Assertions.Expect(select.Locator("option")).ToHaveTextAsync(new[] { "Lunch menu (English)" });
        await Card.GetByRole(AriaRole.Button, new() { Name = "Link", Exact = true }).ClickAsync();

        await Assertions.Expect(Card.GetByTestId("language-version")).ToHaveCountAsync(2);
        await Assertions.Expect(Card.GetByTestId("language-version").Nth(1)).ToContainTextAsync("Lunch menu");
        (await GroupOfAsync(german)).Should().NotBeNull().And.Be(await GroupOfAsync(english));
        // The preview reloads with the language switch
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        await Assertions.Expect(frame.GetByTestId("language-button")).ToHaveTextAsync("DE");

        // "Edit" of the other version opens its settings
        await Card.GetByRole(AriaRole.Link, new() { Name = "Edit the English version" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/edit/{english}$"));
        await Assertions.Expect(Page.Locator("#menuName")).ToHaveValueAsync("Lunch menu");
        await Assertions.Expect(Card.GetByTestId("language-version").Nth(1)).ToContainTextAsync("this menu");

        // Unlink the German version from here: both are on their own again
        await Card.GetByRole(AriaRole.Button, new() { Name = "Unlink the German version" }).ClickAsync();
        await Assertions.Expect(Card.GetByTestId("language-version")).ToHaveCountAsync(1);
        await Assertions.Expect(Card.GetByTestId("language-versions-empty")).ToBeVisibleAsync();
        (await GroupOfAsync(german)).Should().BeNull();
        (await GroupOfAsync(english)).Should().BeNull();
        await Assertions.Expect(frame.GetByTestId("language-button")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task OneMenuPerLanguage_InAGroup()
    {
        var owner = await RegisterOwnerAsync();
        var german = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.German, "Karte", "Gulasch", 12.00m);
        await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.English, "Menu", "Goulash", 12.00m, german);
        await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.English, "Dinner", "Goulash", 12.00m);

        await GotoInteractiveAsync($"/menu/edit/{german}");

        // The language select: English is taken by the other version
        await Assertions.Expect(Page.Locator("#languageSelect option[value='English']")).ToBeDisabledAsync();
        await Assertions.Expect(Page.Locator("#languageSelect option[value='English']")).ToHaveTextAsync("English (another version)");

        // "Add translation" does not offer English
        await Card.GetByRole(AriaRole.Button, new() { Name = "Add translation" }).ClickAsync();
        var options = await Card.Locator("#translationLanguage option").AllTextContentsAsync();
        options.Should().NotContain(o => o.StartsWith("English")).And.NotContain(o => o.StartsWith("German"));
        options.Should().HaveCount(Enum.GetValues<MenuLanguage>().Length - 2);
        await Assertions.Expect(Card.Locator("#translationLanguage")).ToHaveValueAsync("French");
        await Card.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        // "Link existing menu": the other English menu cannot join
        await Card.GetByRole(AriaRole.Button, new() { Name = "Link existing menu" }).ClickAsync();
        await Assertions.Expect(Card.GetByTestId("language-versions-error")).ToContainTextAsync("There is no menu to link");
    }

    [Fact]
    public async Task DeletingOneVersion_KeepsTheOthersLinked()
    {
        var owner = await RegisterOwnerAsync();
        var german = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.German, "Karte DE", "Gulasch", 12.00m);
        var english = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.English, "Karte EN", "Goulash", 12.00m, german);
        var french = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.French, "Karte FR", "Goulasch", 12.00m, german);
        var group = await GroupOfAsync(german);

        await GotoInteractiveAsync("/menus");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete menu Karte EN" }).ClickAsync();
        var dialog = Page.Locator("#confirmationModal");
        await Assertions.Expect(dialog).ToContainTextAsync("Its other language versions (German, French) stay.");
        await dialog.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(MenuCardLink(Page, "Karte EN")).ToHaveCountAsync(0);

        await using var db = App.CreateDbContext(Guid.Empty);
        (await db.Menus.IgnoreQueryFilters().AnyAsync(m => m.Id == english)).Should().BeFalse();
        (await GroupOfAsync(german)).Should().Be(group);
        (await GroupOfAsync(french)).Should().Be(group);

        // Guests of the German version now see German and French only
        await OpenMenuAsync(german);
        await Page.GetByTestId("language-button").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("language-sheet").GetByTestId("language-option").Locator(".gm-langlist-name"))
            .ToHaveTextAsync(new[] { "Deutsch", "Français" });
    }
}
