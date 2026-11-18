using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Back-office usability package P2: order of categories and items (back office and guest menu; since AP11 by drag
/// handle, here through its keyboard path), the item table (euro prices, labels, icon actions, "Delete All"), empty
/// states, the optional description, inline renaming of categories and the hidden Ollama generator button.
/// </summary>
public class CategoryAndItemUxTests : BackOfficeTestBase
{
    public CategoryAndItemUxTests(E2EFixture fixture) : base(fixture) { }

    private ILocator CategoryRows => Page.GetByTestId("category-row");
    private ILocator CategoryNames => CategoryRows.Locator("td a");
    private ILocator ItemRows => Page.GetByTestId("menu-item-row");

    private ILocator ExactButton(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private async Task<Guid> OwnerIdAsync(string email)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
    }

    /// <summary>Seeds a menu with one category and the given items (all SortOrder 0) and returns the ids.</summary>
    private async Task<(Guid MenuId, Guid CategoryId)> SeedMenuAsync(Guid ownerId, string menuName, string categoryName, params MenuItem[] items)
    {
        var menuId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = menuName, Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German,
                Categories = new List<Category> { new() { Id = categoryId, Name = categoryName, MenuId = menuId } }
            });
            foreach (var item in items)
            {
                item.CategoryId = categoryId;
                db.MenuItems.Add(item);
            }
            return Task.CompletedTask;
        });
        return (menuId, categoryId);
    }

    // --- Order of categories ---

    [Fact]
    public async Task Categories_CanBeReordered_OrderPersists_AndGuestsSeeIt()
    {
        var email = await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Reihenfolge Menü");
        await CreateCategoryViaUiAsync(menuId, "Vorspeisen");
        await CreateCategoryViaUiAsync(menuId, "Hauptspeisen");
        await CreateCategoryViaUiAsync(menuId, "Desserts");

        // New categories are appended (creation order, not alphabetical); every row has a grip handle, no arrows
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Hauptspeisen", "Desserts" });
        await Assertions.Expect(CategoryRows.GetByTestId("sort-handle")).ToHaveCountAsync(3);
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Move .* (up|down)$") })).ToHaveCountAsync(0);

        // Move the third category to the top with the keyboard path of the handle (↑ twice; wait for each step)
        var handle = ExactButton("Reorder Desserts");
        await handle.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Desserts", "Hauptspeisen" });
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });
        // Already at the top: nothing moves
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Desserts is already first.");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });

        // Persisted
        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });

        // The items page starts with the first category and lists them in this order
        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Assertions.Expect(Page.Locator("h1")).ToContainTextAsync("Desserts");

        // Guests see the categories in the same order (only categories with items are shown)
        var ownerId = await OwnerIdAsync(email);
        await App.SeedAsync(ownerId, async db =>
        {
            foreach (var category in await db.Categories.Where(c => c.MenuId == menuId).ToListAsync())
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = $"Gericht {category.Name}", Description = "", Price = 5m, CategoryId = category.Id });
        });
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(guest.Locator("[data-testid='category-section'] h2")).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });
        await Assertions.Expect(guest.GetByTestId("category-chip")).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });
    }

    [Fact]
    public async Task CategoriesPage_LinksToItemsAllergensAndCombos_AndHasNoPlaceholderCard()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Links Menü");
        await CreateCategoryViaUiAsync(menuId, "Suppen");

        // The links are the tabs of the menu context bar (the page's own link row is gone)
        await Assertions.Expect(Page.GetByTestId("category-links")).ToHaveCountAsync(0);
        var links = Page.GetByTestId("menu-tabs");
        await Assertions.Expect(links.GetByRole(AriaRole.Link, new() { Name = "Items" })).ToHaveAttributeAsync("href", $"/MenuItems/{menuId}");
        await Assertions.Expect(links.GetByRole(AriaRole.Link, new() { Name = "Allergens" })).ToHaveAttributeAsync("href", $"/allergens/{menuId}");
        await Assertions.Expect(links.GetByRole(AriaRole.Link, new() { Name = "Combos" })).ToHaveAttributeAsync("href", $"/combos/{menuId}");
        await Assertions.Expect(Page.GetByText("Show something useful here")).ToHaveCountAsync(0);

        // The category name links to its items page and the item count is shown
        await Assertions.Expect(CategoryRows.First).ToContainTextAsync("0 items");
        // Client-side navigation within the circuit
        await CategoryNames.First.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/MenuItems/Category/", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.Locator("h1")).ToContainTextAsync("Suppen");
    }

    [Fact]
    public async Task Category_IsRenamedInTheRow_EnterSaves_EscapeAndCancelKeepTheName()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Umbenennen Menü");
        await CreateCategoryViaUiAsync(menuId, "Vorspeisen");
        await CreateCategoryViaUiAsync(menuId, "Desserts");

        // Inline: the row turns into a small form (no modal any more), the name field gets the focus
        var row = CategoryRows.First;
        var form = Page.GetByTestId("category-edit");
        var nameField = form.Locator("#Category\\.Name");
        async Task OpenRenameAsync()
        {
            await row.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit ") }).ClickAsync();
            await Assertions.Expect(form).ToBeVisibleAsync();
            await Assertions.Expect(nameField).ToBeFocusedAsync();
        }

        // Enter saves
        await OpenRenameAsync();
        await nameField.FillAsync("Starters");
        await nameField.PressAsync("Enter");
        await Assertions.Expect(form).ToHaveCountAsync(0);
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Starters", "Desserts" });
        // The focus returns to the row's edit button
        await Assertions.Expect(ExactButton("Edit Starters")).ToBeFocusedAsync();

        // Escape cancels
        await OpenRenameAsync();
        await nameField.FillAsync("Nicht speichern");
        await nameField.PressAsync("Escape");
        await Assertions.Expect(form).ToHaveCountAsync(0);
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Starters", "Desserts" });

        // "Cancel" too
        await OpenRenameAsync();
        await nameField.FillAsync("Auch nicht");
        await form.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(form).ToHaveCountAsync(0);
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Starters", "Desserts" });

        // Same validation messages as before: required, unique within the menu
        await OpenRenameAsync();
        await nameField.FillAsync("");
        await nameField.PressAsync("Enter");
        await Assertions.Expect(form.GetByText("Name is required.")).ToBeVisibleAsync();
        await nameField.FillAsync("Desserts");
        await nameField.PressAsync("Enter");
        await Assertions.Expect(form.GetByText("This name already exists.")).ToBeVisibleAsync();
        await nameField.PressAsync("Escape");

        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Starters", "Desserts" });
    }

    // --- Order of items ---

    [Fact]
    public async Task Items_CanBeReorderedWithinCategory_OrderPersists_AndGuestsSeeIt()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Item Reihenfolge");
        await CreateCategoryViaUiAsync(menuId, "Suppen");
        await CreateMenuItemViaUiAsync(menuId, "Tomatensuppe", "Mit Basilikum", 5.20m);
        await CreateMenuItemViaUiAsync(menuId, "Brotkorb", "Hausgemacht", 3.00m);

        // Appended in creation order (alphabetically "Brotkorb" would come first)
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Tomatensuppe", "Brotkorb" });
        await Assertions.Expect(ItemRows.GetByTestId("sort-handle")).ToHaveCountAsync(2);

        await ExactButton("Reorder Brotkorb").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Brotkorb", "Tomatensuppe" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Brotkorb to position 1 of 2.");
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Brotkorb is already first.");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Brotkorb", "Tomatensuppe" });

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(guest.Locator("[data-testid='category-section'][data-category='Suppen'] h3")).ToHaveTextAsync(new[] { "Brotkorb", "Tomatensuppe" });
    }

    // --- Item table ---

    [Fact]
    public async Task ItemTable_ShowsEuroPrices_Labels_AndIconActions()
    {
        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);
        var menuId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var schnitzelId = Guid.NewGuid();
        var strudelId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            var a = new Allergen { Id = Guid.NewGuid(), Code = "A", Name = "Gluten", MenuId = menuId };
            var c = new Allergen { Id = Guid.NewGuid(), Code = "C", Name = "Eier", MenuId = menuId };
            var g = new Allergen { Id = Guid.NewGuid(), Code = "G", Name = "Milch", MenuId = menuId };
            db.Allergens.AddRange(a, c, g);
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = "Preis Menü", Description = "", Theme = DesignTheme.Classic, Language = MenuLanguage.German,
                Categories = new List<Category> { new() { Id = categoryId, Name = "Hauptspeisen", MenuId = menuId } }
            });
            db.MenuItems.Add(new MenuItem { Id = schnitzelId, Name = "Wiener Schnitzel", Description = "Mit Petersilerdäpfeln", Price = 24.90m, CategoryId = categoryId, Allergens = { g, a, c } });
            db.MenuItems.Add(new MenuItem { Id = strudelId, Name = "Apfelstrudel", Description = "", Price = 6.80m, CategoryId = categoryId, IsVegetarian = true, IsVegan = true });
            db.MenuCombos.Add(new MenuCombo
            {
                Id = Guid.NewGuid(), Name = "Mittagsmenü", Description = "", ComboPrice = 29.90m, MenuId = menuId,
                TriggerMenuItemId = schnitzelId, Items = { new MenuComboItem { MenuItemId = strudelId } }
            });
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        var schnitzel = ItemRows.Filter(new() { HasText = "Wiener Schnitzel" });
        var strudel = ItemRows.Filter(new() { HasText = "Apfelstrudel" });

        // Euro like the guest menu, never the server culture's currency
        await Assertions.Expect(schnitzel).ToContainTextAsync("€ 24.90");
        await Assertions.Expect(strudel).ToContainTextAsync("€ 6.80");
        await Assertions.Expect(Page.GetByTestId("menu-items")).Not.ToContainTextAsync("$");

        // Labels: allergen codes sorted, dietary labels; the image column is gone
        await Assertions.Expect(schnitzel.GetByTestId("allergen-chip")).ToHaveTextAsync(new[] { "A", "C", "G" });
        await Assertions.Expect(schnitzel.GetByTestId("allergen-chip").First).ToHaveAttributeAsync("title", "Allergen A: Gluten");
        await Assertions.Expect(strudel.GetByTestId("item-labels")).ToContainTextAsync("Vegan");
        await Assertions.Expect(Page.Locator("th", new() { HasTextString = "Image" })).ToHaveCountAsync(0);

        // Icon actions carry the item name for screen readers (edit is a link to the item's edit page)
        await Assertions.Expect(Link("Edit Wiener Schnitzel", exact: true)).ToBeVisibleAsync();
        await Assertions.Expect(ExactButton("Delete Wiener Schnitzel")).ToBeVisibleAsync();
        await OpenItemEditorAsync("Apfelstrudel");
        await Assertions.Expect(ItemEditor.Locator("#MenuItem\\.Name")).ToHaveValueAsync("Apfelstrudel");
        await ItemEditor.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(ItemEditor).ToHaveCountAsync(0);
        await Assertions.Expect(strudel).ToBeVisibleAsync();

        // The Ollama generator is switched off outside Development
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Generate Menu Items" })).ToHaveCountAsync(0);

        // Combos page and combo editor use the same format
        await GotoInteractiveAsync($"/combos/{menuId}");
        await Assertions.Expect(Page.GetByText("€ 29.90")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("$29.90")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DeleteAll_AsksForConfirmation_NamingCategoryAndCount()
    {
        var email = await RegisterNewOwnerAsync();
        var (menuId, _) = await SeedMenuAsync(await OwnerIdAsync(email), "Lösch Menü", "Getränke",
            new MenuItem { Id = Guid.NewGuid(), Name = "Cola", Description = "0,33 l", Price = 3.50m },
            new MenuItem { Id = Guid.NewGuid(), Name = "Wasser", Description = "", Price = 2.50m });

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Assertions.Expect(ItemRows).ToHaveCountAsync(2);

        var confirmation = Page.Locator("#confirmationModal");
        await ExactButton("Delete All").ClickAsync();
        await Assertions.Expect(confirmation).ToBeVisibleAsync();
        await Assertions.Expect(confirmation).ToContainTextAsync("Delete all 2 items in \"Getränke\"");
        await confirmation.Locator("#cancelBtn").ClickAsync();
        await Assertions.Expect(confirmation).ToBeHiddenAsync();
        await Assertions.Expect(ItemRows).ToHaveCountAsync(2);

        await ExactButton("Delete All").ClickAsync();
        await Assertions.Expect(confirmation).ToBeVisibleAsync();
        await confirmation.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("menu-items-empty")).ToContainTextAsync("No items in Getränke yet");
        await Assertions.Expect(ItemRows).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Items_WithGuestClicks_CanBeDeleted_SinglyAndAll()
    {
        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);
        var cola = new MenuItem { Id = Guid.NewGuid(), Name = "Cola", Description = "0,33 l", Price = 3.50m };
        var wasser = new MenuItem { Id = Guid.NewGuid(), Name = "Wasser", Description = "", Price = 2.50m };
        var (menuId, _) = await SeedMenuAsync(ownerId, "Klick Menü", "Getränke", cola, wasser);
        // MenuItemClick.MenuItemId is a NoAction foreign key; the back office must remove the clicks first
        await App.SeedAsync(ownerId, db =>
        {
            db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = "s1", MenuId = menuId, MenuItemId = cola.Id });
            db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = "s2", MenuId = menuId, MenuItemId = wasser.Id });
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        var confirmation = Page.Locator("#confirmationModal");
        await ExactButton("Delete Cola").ClickAsync();
        await confirmation.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(ItemRows).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByText("could not be deleted")).ToHaveCountAsync(0);

        await ExactButton("Delete All").ClickAsync();
        await confirmation.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("menu-items-empty")).ToBeVisibleAsync();

        await using var db = App.CreateDbContext(ownerId);
        (await db.MenuItems.AnyAsync(i => i.Id == cola.Id || i.Id == wasser.Id)).Should().BeFalse();
        (await db.MenuItemClicks.AnyAsync(c => c.MenuId == menuId)).Should().BeFalse();
    }

    // --- Empty states ---

    [Fact]
    public async Task MenuWithoutCategories_ShowsAddCategoryCta()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Leeres Menü");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        var empty = Page.GetByTestId("menu-items-no-categories");
        await Assertions.Expect(empty).ToContainTextAsync("No categories yet");
        var cta = empty.GetByRole(AriaRole.Link, new() { Name = "Add a category" });
        await Assertions.Expect(cta).ToHaveAttributeAsync("href", $"/Categories/{menuId}");

        await cta.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/Categories/{menuId}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByTestId("categories-empty")).ToContainTextAsync("No categories yet");
    }

    [Fact]
    public async Task CategoryWithoutItems_ShowsEmptyCardWithNewEntryButton()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Leere Kategorie");
        await CreateCategoryViaUiAsync(menuId, "Desserts");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        var empty = Page.GetByTestId("menu-items-empty");
        await Assertions.Expect(empty).ToContainTextAsync("No items in Desserts yet");
        await Assertions.Expect(empty.GetByRole(AriaRole.Link, new() { Name = "New Menu Entry" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "New Menu Entry" })).ToHaveCountAsync(1);
        await Assertions.Expect(ExactButton("Delete All")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Generate Menu Items" })).ToHaveCountAsync(0);
    }

    // --- Optional description ---

    [Fact]
    public async Task ItemWithoutDescription_CanBeSaved_AndShowsOnGuestPage()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Getränke Menü");
        await CreateCategoryViaUiAsync(menuId, "Getränke");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Link("New Menu Entry").ClickAsync();
        await Assertions.Expect(ItemEditor).ToBeVisibleAsync();
        await Assertions.Expect(ItemEditor.Locator("label[for='MenuItem.Description']")).Not.ToContainTextAsync("*");
        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync("Almdudler 0,35 l");
        await ItemEditor.Locator("#MenuItem\\.Price").FillAsync("3.60");
        await SaveItemAndReturnAsync();

        var row = ItemRows.Filter(new() { HasText = "Almdudler 0,35 l" });
        await Assertions.Expect(row).ToContainTextAsync("€ 3.60");

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        // Without description the list row has no empty paragraph either
        await Assertions.Expect(ItemEntry(guest, "Almdudler 0,35 l").Locator(".gm-row-desc")).ToHaveCountAsync(0);
        await ClickMenuItemAsync(guest, "Almdudler 0,35 l");
        var sheet = ItemSheetOf(guest);
        await Assertions.Expect(sheet.GetByTestId("item-price")).ToHaveTextAsync("€ 3,60");
        // No empty description paragraph (with its margin) in the item sheet
        await Assertions.Expect(sheet.Locator(".gm-detail-desc")).ToHaveCountAsync(0);
    }
}
