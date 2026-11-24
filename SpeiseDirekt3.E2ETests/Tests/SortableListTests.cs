using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP11: drag-and-drop sorting with grip handles (Shared/Sortable/SortableList, js/sortable-list.js) for categories,
/// items and the item editor's price variants — with the mouse, with a finger on a phone and with the arrow keys on a
/// focused handle (announced in the live region); the order is saved on drop, a failed save rolls back with a message.
/// </summary>
public class SortableListTests : BackOfficeTestBase
{
    public SortableListTests(E2EFixture fixture) : base(fixture) { }

    private ILocator CategoryRows => Page.GetByTestId("category-row");
    private ILocator CategoryNames => CategoryRows.Locator("td a");
    private ILocator ItemRows => Page.GetByTestId("menu-item-row");

    private ILocator CategoryRow(string name) => CategoryRows.Filter(new() { Has = Page.Locator("a", new() { HasTextString = name }) });

    /// <summary>A menu with three categories (Vorspeisen, Hauptspeisen, Desserts; one item each) and three soups.</summary>
    private async Task<(Guid OwnerId, Guid MenuId, Guid SoupsId)> SeedAsync(string menuName)
    {
        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);
        var menuId = Guid.NewGuid();
        var soupsId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            var starters = new Category { Id = soupsId, Name = "Vorspeisen", MenuId = menuId, SortOrder = 0 };
            var mains = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId, SortOrder = 1 };
            var desserts = new Category { Id = Guid.NewGuid(), Name = "Desserts", MenuId = menuId, SortOrder = 2 };
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = menuName, Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German,
                Categories = new List<Category> { starters, mains, desserts }
            });
            var order = 0;
            foreach (var name in new[] { "Tomatensuppe", "Frittatensuppe", "Kürbissuppe" })
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = name, Description = "", Price = 5m, CategoryId = soupsId, SortOrder = order++ });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Schnitzel", Description = "", Price = 19m, CategoryId = mains.Id });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Strudel", Description = "", Price = 6m, CategoryId = desserts.Id });
            return Task.CompletedTask;
        });
        return (ownerId, menuId, soupsId);
    }

    private async Task<string[]> StoredCategoryOrderAsync(Guid ownerId, Guid menuId)
    {
        await using var db = App.CreateDbContext(ownerId);
        return (await db.Categories.Where(c => c.MenuId == menuId).InDisplayOrder().ToListAsync()).Select(c => c.Name).ToArray();
    }

    private async Task ExpectGuestCategoryOrderAsync(Guid menuId, params string[] names)
    {
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(guest.Locator("[data-testid='category-section'] h2")).ToHaveTextAsync(names);
    }

    [Fact]
    public async Task Categories_DraggedWithTheMouse_OrderIsSaved_AndGuestsSeeIt()
    {
        var (ownerId, menuId, _) = await SeedAsync("Drag Menü");
        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Hauptspeisen", "Desserts" });
        // Every row has a grip; the arrow buttons are gone
        await Assertions.Expect(CategoryRows.GetByTestId("sort-handle")).ToHaveCountAsync(3);
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reorder Desserts", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Move .* (up|down)$") })).ToHaveCountAsync(0);

        // Desserts to the top
        await DragRowAsync(CategoryRow("Desserts"), CategoryRow("Vorspeisen"));
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Desserts to position 1 of 3.");

        // Vorspeisen to the end
        await DragRowAsync(CategoryRow("Vorspeisen"), CategoryRow("Hauptspeisen"));
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Hauptspeisen", "Vorspeisen" });
        // The list shows the new order at once (optimistic); the announcement follows the save
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Vorspeisen to position 3 of 3.");

        // Saved once per drop (SortOrder 0..n-1) and still there after a reload
        await Assertions.Expect(Page.Locator(".sd-sort-placeholder")).ToHaveCountAsync(0);
        (await StoredCategoryOrderAsync(ownerId, menuId)).Should().Equal("Desserts", "Hauptspeisen", "Vorspeisen");
        await using (var db = App.CreateDbContext(ownerId))
            (await db.Categories.Where(c => c.MenuId == menuId).OrderBy(c => c.SortOrder).Select(c => c.SortOrder).ToListAsync()).Should().Equal(0, 1, 2);
        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Hauptspeisen", "Vorspeisen" });

        await ExpectGuestCategoryOrderAsync(menuId, "Desserts", "Hauptspeisen", "Vorspeisen");
    }

    [Fact]
    public async Task WhileDragging_TheRowIsLifted_WithAPlaceholder_AndEscapeCancels()
    {
        var (ownerId, menuId, _) = await SeedAsync("Lift Menü");
        await GotoInteractiveAsync($"/Categories/{menuId}");

        var row = CategoryRow("Vorspeisen");
        var handle = HandleOf(row);
        await WaitForSortableAsync(handle);
        var box = (await handle.BoundingBoxAsync())!;
        var target = (await CategoryRow("Desserts").BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, target.Y + target.Height / 2 + 10, new() { Steps = 10 });

        await Assertions.Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-sort-lifted"));
        await Assertions.Expect(Page.GetByTestId("sort-placeholder")).ToHaveCountAsync(1);
        (await row.EvaluateAsync<string>("r => getComputedStyle(r).transform")).Should().NotBe("none");
        // The rows it passed moved up out of its way; the placeholder slides into the last slot
        double placeholderTop = 0;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            placeholderTop = await Page.GetByTestId("sort-placeholder").EvaluateAsync<double>("p => p.getBoundingClientRect().top");
            if (Math.Abs(placeholderTop - target.Y) < 3) break;
            await Page.WaitForTimeoutAsync(50);
        }
        placeholderTop.Should().BeApproximately(target.Y, 3);
        (await CategoryRow("Desserts").EvaluateAsync<string>("r => getComputedStyle(r).transform")).Should().NotBe("none");

        await Page.Keyboard.PressAsync("Escape");
        await Page.Mouse.UpAsync();
        await Assertions.Expect(Page.GetByTestId("sort-placeholder")).ToHaveCountAsync(0);
        await Assertions.Expect(row).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-sort-lifted"));
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Hauptspeisen", "Desserts" });
        (await StoredCategoryOrderAsync(ownerId, menuId)).Should().Equal("Vorspeisen", "Hauptspeisen", "Desserts");
    }

    [Fact]
    public async Task Items_DraggedWithTheMouse_OrderIsSaved_AndGuestsSeeIt()
    {
        var (ownerId, menuId, soupsId) = await SeedAsync("Item Drag Menü");
        await GotoInteractiveAsync($"/MenuItems/Category/{soupsId}");
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Tomatensuppe", "Frittatensuppe", "Kürbissuppe" });

        ILocator Row(string name) => ItemRows.Filter(new() { HasText = name });
        await DragRowAsync(Row("Kürbissuppe"), Row("Tomatensuppe"));
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Kürbissuppe", "Tomatensuppe", "Frittatensuppe" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Kürbissuppe to position 1 of 3.");

        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.Where(i => i.CategoryId == soupsId).InDisplayOrder().Select(i => i.Name).ToListAsync())
                .Should().Equal("Kürbissuppe", "Tomatensuppe", "Frittatensuppe");
        await GotoInteractiveAsync($"/MenuItems/Category/{soupsId}");
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Kürbissuppe", "Tomatensuppe", "Frittatensuppe" });

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(guest.Locator("[data-testid='category-section'][data-category='Vorspeisen'] h3"))
            .ToHaveTextAsync(new[] { "Kürbissuppe", "Tomatensuppe", "Frittatensuppe" });
    }

    [Fact]
    public async Task Variants_DraggedInTheItemEditor_AreSavedInThatOrder()
    {
        var (ownerId, _, soupsId) = await SeedAsync("Varianten Drag");
        var itemId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = itemId, Name = "Hausbier", Description = "", Price = 3.40m, CategoryId = soupsId, SortOrder = 9,
                PriceOptions =
                {
                    new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,5 l", Price = 5.90m, SortOrder = 0 },
                    new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,3 l", Price = 4.20m, SortOrder = 1 },
                    new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,2 l", Price = 3.40m, SortOrder = 2 }
                }
            });
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync($"/MenuItems/item/{itemId}/edit");
        var rows = ItemEditor.GetByTestId("variant-row");
        var names = rows.Locator("input[aria-label^='Name of']");
        await Assertions.Expect(names).ToHaveCountAsync(3);
        await Assertions.Expect(HandleOf(rows.Nth(2))).ToHaveAccessibleNameAsync("Reorder variant 0,2 l");

        // Smallest first: drag 0,2 l to the top, then 0,5 l to the end
        await DragRowAsync(rows.Nth(2), rows.Nth(0));
        await Assertions.Expect(names.Nth(0)).ToHaveValueAsync("0,2 l");
        await Assertions.Expect(names.Nth(1)).ToHaveValueAsync("0,5 l");
        await DragRowAsync(rows.Nth(1), rows.Nth(2));
        await Assertions.Expect(names.Nth(0)).ToHaveValueAsync("0,2 l");
        await Assertions.Expect(names.Nth(1)).ToHaveValueAsync("0,3 l");
        await Assertions.Expect(names.Nth(2)).ToHaveValueAsync("0,5 l");
        // The labels follow the rows' new positions
        await Assertions.Expect(rows.Nth(0).GetByLabel("Name of variant 1")).ToHaveValueAsync("0,2 l");

        // Not saved before "Save"
        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.Include(i => i.PriceOptions).SingleAsync(i => i.Id == itemId)).Variants().Select(o => o.Name)
                .Should().Equal("0,5 l", "0,3 l", "0,2 l");

        await SaveItemAndReturnAsync();
        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.Include(i => i.PriceOptions).SingleAsync(i => i.Id == itemId)).Variants().Select(o => (o.Name, o.SortOrder))
                .Should().Equal(("0,2 l", 0), ("0,3 l", 1), ("0,5 l", 2));
    }

    [Fact]
    public async Task Keyboard_ArrowKeysOnTheHandle_MoveTheRow_AndAnnounceIt()
    {
        var (ownerId, menuId, _) = await SeedAsync("Tastatur Menü");
        await GotoInteractiveAsync($"/Categories/{menuId}");
        var handle = Page.GetByRole(AriaRole.Button, new() { Name = "Reorder Desserts", Exact = true });
        await Assertions.Expect(handle).ToHaveAttributeAsync("aria-describedby", "sd-sort-help");
        await Assertions.Expect(Page.Locator("#sd-sort-help")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("arrow keys"));

        await FocusHandleAsync(handle);
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Desserts", "Hauptspeisen" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Desserts to position 2 of 3.");
        // The focus stays on the moved row's handle, so the next key moves it again
        await Assertions.Expect(handle).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Desserts to position 1 of 3.");
        await Assertions.Expect(handle).ToBeFocusedAsync();

        // At the top: nothing moves, the boundary is announced
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Desserts is already first.");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });

        await Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Desserts", "Hauptspeisen" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Desserts to position 2 of 3.");
        (await StoredCategoryOrderAsync(ownerId, menuId)).Should().Equal("Vorspeisen", "Desserts", "Hauptspeisen");
    }

    /// <summary>
    /// The page renders again while js/sortable-list.js is still loading: the script is attached once. Attached once per
    /// render, every listener ran that often — a press on a handle lifted the row three times, and with two attachments
    /// one ↑ moved the row up and straight back (the item page's variants, found on Windows).
    /// </summary>
    [Fact]
    public async Task Script_IsAttachedOnce_WhenThePageRendersAgainWhileItLoads()
    {
        var (ownerId, _, soupsId) = await SeedAsync("Laden Menü");
        var itemId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            db.MenuItems.Add(new MenuItem
            {
                Id = itemId, Name = "Hausbier", Description = "", Price = 3.40m, CategoryId = soupsId, SortOrder = 9,
                PriceOptions =
                {
                    new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,5 l", Price = 5.90m, SortOrder = 0 },
                    new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,25 l", Price = 3.40m, SortOrder = 1 }
                }
            });
            return Task.CompletedTask;
        });

        // Hold the script back until the page has rendered twice more
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // (the import map points ./js/sortable-list.js to its fingerprinted name)
        await Page.RouteAsync(new System.Text.RegularExpressions.Regex(@"/js/sortable-list(\.[^/]*)?\.js"), async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        await GotoInteractiveAsync($"/MenuItems/item/{itemId}/edit");
        var rows = ItemEditor.GetByTestId("variant-row");
        var names = rows.Locator("input[aria-label^='Name of']");
        // Each change re-renders the list (the handles' labels follow the names); the second one is processed after
        // the first render's OnAfterRenderAsync, so that one has asked for the script again by then
        await names.Nth(0).FillAsync("0,4 l");
        await names.Nth(0).PressAsync("Tab");
        await Assertions.Expect(HandleOf(rows.Nth(0))).ToHaveAccessibleNameAsync("Reorder variant 0,4 l");
        await names.Nth(1).FillAsync("0,2 l");
        await names.Nth(1).PressAsync("Tab");
        await Assertions.Expect(HandleOf(rows.Nth(1))).ToHaveAccessibleNameAsync("Reorder variant 0,2 l");
        release.SetResult();

        // Once the script listens, ↑ on the first row only announces that it is first (a round trip, after which any
        // further attach that was queued behind the first one would have run too)
        await FocusHandleAsync(HandleOf(rows.Nth(0)));
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("variant 0,4 l is already first.");

        // One set of listeners: a press on a handle lifts the row once (one placeholder marks its slot)
        var handle = HandleOf(rows.Nth(1));
        await handle.ScrollIntoViewIfNeededAsync();
        var box = (await handle.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.DownAsync();
        await Assertions.Expect(Page.GetByTestId("sort-placeholder")).ToHaveCountAsync(1);
        await Page.Keyboard.PressAsync("Escape");
        await Page.Mouse.UpAsync();
        await Assertions.Expect(Page.GetByTestId("sort-placeholder")).ToHaveCountAsync(0);

        // And one ↑ moves the row once
        await handle.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved variant 0,2 l to position 1 of 2.");
        // The next ↑ finds it at the top (moved twice, it would be second again and move once more)
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("variant 0,2 l is already first.");
        await Assertions.Expect(names.Nth(0)).ToHaveValueAsync("0,2 l");
        await Assertions.Expect(names.Nth(1)).ToHaveValueAsync("0,4 l");
    }

    /// <summary>
    /// The list is left while js/sortable-list.js is still loading: the late script is let go. Attached anyway, the
    /// list handed JS its disposed .NET reference, the exception ended the circuit, and the next page (here the item
    /// page) looked fine but no longer reacted — no Save, no Cancel (found on Windows under the parallel test load).
    /// </summary>
    [Fact]
    public async Task LeavingTheList_WhileTheScriptLoads_KeepsTheNextPageWorking()
    {
        var (_, _, soupsId) = await SeedAsync("Verlassen Menü");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var script = new System.Text.RegularExpressions.Regex(@"/js/sortable-list(\.[^/]*)?\.js");
        await Page.RouteAsync(script, async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        await GotoInteractiveAsync($"/MenuItems/Category/{soupsId}");
        await Assertions.Expect(HandleOf(ItemRows.First)).ToBeVisibleAsync();

        // Off to the item page while the list still waits for its script, then the script arrives
        await Link("Edit Tomatensuppe", exact: true).ClickAsync();
        await Assertions.Expect(ItemEditor).ToBeVisibleAsync();
        var loaded = Page.WaitForResponseAsync(script);
        release.SetResult();
        await (await loaded).FinishedAsync();

        // The circuit is alive: the page notices a change and Cancel leaves it
        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync("Tomatencremesuppe");
        await ItemEditor.Locator("#MenuItem\\.Name").PressAsync("Tab");
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("Unsaved changes");
        await Page.GetByTestId("item-savebar").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/MenuItems/Category/{soupsId}\\?highlight=", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task FailedSave_RollsBack_WithAMessage()
    {
        var (ownerId, menuId, _) = await SeedAsync("Fehler Menü");
        await GotoInteractiveAsync($"/Categories/{menuId}");

        // The database refuses every SortOrder change of this menu's categories (a SQLite trigger, this test class's DB only)
        await using (var db = App.CreateDbContext(ownerId))
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER e2e_refuse_sort BEFORE UPDATE OF SortOrder ON Categories BEGIN SELECT RAISE(ABORT, 'E2E: sort order refused'); END;");
        try
        {
            await DragRowAsync(CategoryRow("Desserts"), CategoryRow("Vorspeisen"));
            await Assertions.Expect(Page.GetByTestId("order-error")).ToContainTextAsync("could not be saved");
            await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Vorspeisen", "Hauptspeisen", "Desserts" });
            await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Desserts could not be moved. The previous order is shown again.");
            await Assertions.Expect(CategoryRows.Locator("[style*='transform']")).ToHaveCountAsync(0);
            (await StoredCategoryOrderAsync(ownerId, menuId)).Should().Equal("Vorspeisen", "Hauptspeisen", "Desserts");
        }
        finally
        {
            await using var db = App.CreateDbContext(ownerId);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS e2e_refuse_sort;");
        }

        // Once the database accepts it again, the same drag works
        await DragRowAsync(CategoryRow("Desserts"), CategoryRow("Vorspeisen"));
        await Assertions.Expect(CategoryNames).ToHaveTextAsync(new[] { "Desserts", "Vorspeisen", "Hauptspeisen" });
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("Moved Desserts to position 1 of 3.");
        await Assertions.Expect(Page.GetByTestId("order-error")).ToHaveCountAsync(0);
        (await StoredCategoryOrderAsync(ownerId, menuId)).Should().Equal("Desserts", "Vorspeisen", "Hauptspeisen");
    }

    [Fact]
    public async Task Phone_TouchDragOnTheHandle_ReordersItems_WhileTheRestOfTheRowScrollsThePage()
    {
        var (ownerId, _, soupsId) = await SeedAsync("Handy Drag");
        // Enough rows that the page can scroll
        await App.SeedAsync(ownerId, db =>
        {
            for (var i = 1; i <= 12; i++)
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = $"Suppe {i:00}", Description = "Eine Suppe mit etwas Text, damit die Karte zwei Zeilen hat.", Price = 4m, CategoryId = soupsId, SortOrder = 10 + i });
            return Task.CompletedTask;
        });

        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();
        await GotoInteractiveAsync(phone, $"/MenuItems/Category/{soupsId}");

        var cards = phone.GetByTestId("menu-item-card");
        ILocator Card(string name) => cards.Filter(new() { HasText = name });
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("Tomatensuppe");
        await Assertions.Expect(HandleOf(Card("Frittatensuppe"))).ToBeVisibleAsync();
        (await HandleOf(Card("Frittatensuppe")).EvaluateAsync<string>("h => getComputedStyle(h).touchAction")).Should().Be("none");

        // A finger on the grip drags at once (no long press)
        await TouchDragRowAsync(phone, Card("Frittatensuppe"), Card("Tomatensuppe"));
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("Frittatensuppe");
        await Assertions.Expect(cards.Nth(1)).ToContainTextAsync("Tomatensuppe");
        await Assertions.Expect(Announcer(phone)).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^Moved Frittatensuppe to position 1 of "));
        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.Where(i => i.CategoryId == soupsId).InDisplayOrder().Select(i => i.Name).Take(3).ToListAsync())
                .Should().Equal("Frittatensuppe", "Tomatensuppe", "Kürbissuppe");

        // The same gesture on the card's text scrolls the page and moves nothing
        await phone.EvaluateAsync("() => window.scrollTo(0, 0)");
        var text = cards.Nth(3).Locator("p").First;
        await text.ScrollIntoViewIfNeededAsync();
        var before = await phone.EvaluateAsync<double>("() => window.scrollY");
        var box = (await text.BoundingBoxAsync())!;
        await TouchDragAsync(phone, box.X + box.Width / 2, box.Y + box.Height / 2, box.Y + box.Height / 2 - 250);
        await phone.WaitForTimeoutAsync(400);
        (await phone.EvaluateAsync<double>("() => window.scrollY")).Should().BeGreaterThan(before + 50, "a touch outside the grip still scrolls the page");
        await Assertions.Expect(phone.GetByTestId("sort-placeholder")).ToHaveCountAsync(0);
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("Frittatensuppe");
        await Assertions.Expect(cards.Nth(1)).ToContainTextAsync("Tomatensuppe");
    }

    /// <summary>
    /// AP14: every team member is a manager with the edit permissions, so a manager gets the handles and the sort script
    /// like the owner (the list without handles is left for an account without edit rights, which no role is now).
    /// </summary>
    [Fact]
    public async Task AManager_GetsTheHandles_AndTheSortScript()
    {
        var (ownerId, menuId, soupsId) = await SeedAsync("Team Menü");
        var member = await SeedTeamMemberAsync(ownerId, "Eva");
        var (managerContext, manager) = await NewContextAsync();
        await using var _ = managerContext;
        var scripts = new List<string>();
        manager.Request += (_, r) => { lock (scripts) scripts.Add(new Uri(r.Url).AbsolutePath); };
        await LoginAsync(manager, member.Email);

        await GotoInteractiveAsync(manager, $"/Categories/{menuId}");
        await Assertions.Expect(manager.GetByTestId("category-row")).ToHaveCountAsync(3);
        await Assertions.Expect(manager.GetByTestId("sort-handle")).ToHaveCountAsync(3);
        await GotoInteractiveAsync(manager, $"/MenuItems/Category/{soupsId}");
        await Assertions.Expect(manager.GetByTestId("menu-item-row")).ToHaveCountAsync(3);
        await Assertions.Expect(manager.GetByTestId("menu-item-row").GetByTestId("sort-handle")).ToHaveCountAsync(3);
        await manager.WaitForLoadStateAsync(LoadState.NetworkIdle);
        lock (scripts) scripts.Should().Contain(p => p.Contains("sortable-list"), "a manager may sort");
    }
}
