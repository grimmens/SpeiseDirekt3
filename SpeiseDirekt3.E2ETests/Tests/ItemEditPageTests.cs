using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP11: the item editor is its own page (<c>/MenuItems/Category/{id}/new</c>, <c>/MenuItems/item/{id}/edit</c>) with the
/// menu context bar, a breadcrumb back to the list, calm sections and a sticky save bar. Save and Cancel return to the
/// list, which scrolls to and highlights the item; cancelling never writes; leaving with unsaved changes asks first;
/// read-only for roles without the edit permission.
/// </summary>
public class ItemEditPageTests : BackOfficeTestBase
{
    public ItemEditPageTests(E2EFixture fixture) : base(fixture) { }

    private ILocator ItemRows => Page.GetByTestId("menu-item-row");
    private ILocator Confirmation => Page.Locator("#confirmationModal");

    private async Task<(Guid OwnerId, Guid MenuId, Guid StartersId, Guid MainsId, Guid SoupId)> SeedAsync()
    {
        var ownerId = await OwnerIdAsync(await RegisterNewOwnerAsync());
        var menuId = Guid.NewGuid();
        var startersId = Guid.NewGuid();
        var mainsId = Guid.NewGuid();
        var soupId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            var gluten = new Allergen { Id = Guid.NewGuid(), MenuId = menuId, Code = "A", Name = "Gluten" };
            db.Allergens.AddRange(gluten, new Allergen { Id = Guid.NewGuid(), MenuId = menuId, Code = "G", Name = "Milch" });
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = "Seiten Menü", Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German,
                Categories = new List<Category>
                {
                    new() { Id = startersId, Name = "Vorspeisen", MenuId = menuId, SortOrder = 0 },
                    new() { Id = mainsId, Name = "Hauptspeisen", MenuId = menuId, SortOrder = 1 }
                }
            });
            db.MenuItems.Add(new MenuItem { Id = soupId, Name = "Tomatensuppe", Description = "Mit Basilikum", Price = 5.20m, CategoryId = startersId, SortOrder = 0, Allergens = { gluten } });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Brotkorb", Description = "", Price = 3m, CategoryId = startersId, SortOrder = 1 });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Schnitzel", Description = "", Price = 19m, CategoryId = mainsId, SortOrder = 0 });
            return Task.CompletedTask;
        });
        return (ownerId, menuId, startersId, mainsId, soupId);
    }

    [Fact]
    public async Task NewItem_OpensAsAPage_WithItsCategory_AndIsAppendedAndHighlightedOnSave()
    {
        var (ownerId, menuId, startersId, _, _) = await SeedAsync();
        await GotoInteractiveAsync($"/MenuItems/Category/{startersId}");

        await Link("New Menu Entry").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/Category/{startersId}/new$", RegexOptions.IgnoreCase));
        await Assertions.Expect(ItemEditor).ToBeVisibleAsync();
        // The menu's context bar (Items current) and a breadcrumb back to the list
        await Assertions.Expect(Page.GetByTestId("menu-context-name")).ToHaveTextAsync("Seiten Menü");
        await Assertions.Expect(Page.GetByTestId("menu-tabs").Locator("[aria-current='page']")).ToHaveTextAsync("Items");
        await Assertions.Expect(Page.GetByTestId("item-edit-back")).ToHaveAttributeAsync("href", $"/MenuItems/Category/{startersId}");
        // Calm sections, no dark band; the category is preselected
        foreach (var section in new[] { "basics", "photo", "price", "labels", "allergens" })
            await Assertions.Expect(Page.GetByTestId($"item-section-{section}")).ToBeVisibleAsync();
        await Assertions.Expect(ItemEditor.Locator("#MenuItem\\.CategoryId")).ToHaveValueAsync(startersId.ToString());
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("New item");

        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync("Bruschetta");
        await ItemEditor.Locator("#MenuItem\\.Price").FillAsync("7.40");
        await AllergenCheckbox("A").CheckAsync();
        await ItemEditor.Locator("#MenuItem\\.IsVegan").CheckAsync();
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("Unsaved changes");
        await SaveItemAndReturnAsync();

        // Back on the list: appended at the end, scrolled to and highlighted
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Tomatensuppe", "Brotkorb", "Bruschetta" });
        var row = ItemRows.Filter(new() { HasText = "Bruschetta" });
        await Assertions.Expect(row).ToHaveAttributeAsync("data-highlighted", "true");
        await Assertions.Expect(row).ToHaveClassAsync(new Regex("sd-row-highlight"));
        await Assertions.Expect(row).ToBeInViewportAsync();
        await Assertions.Expect(ItemRows.Filter(new() { HasText = "Brotkorb" })).Not.ToHaveAttributeAsync("data-highlighted", "true");

        await using var db = App.CreateDbContext(ownerId);
        var item = await db.MenuItems.Include(i => i.Allergens).SingleAsync(i => i.Name == "Bruschetta");
        (item.CategoryId, item.SortOrder, item.Price, item.IsVegan).Should().Be((startersId, 2, 7.40m, true));
        item.Allergens.Select(a => a.Code).Should().Equal("A");
    }

    [Fact]
    public async Task Edit_ChangesAreSaved_CancelWritesNothing_AndBothReturnToTheHighlightedItem()
    {
        var (ownerId, _, startersId, _, soupId) = await SeedAsync();
        await GotoInteractiveAsync($"/MenuItems/Category/{startersId}");

        await OpenItemEditorAsync("Tomatensuppe");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/item/{soupId}/edit$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByTestId("page-header")).ToContainTextAsync("Tomatensuppe");
        await Assertions.Expect(ItemEditor.Locator("#MenuItem\\.Name")).ToHaveValueAsync("Tomatensuppe");
        await Assertions.Expect(AllergenCheckbox("A")).ToBeCheckedAsync();
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("No changes yet");

        // Cancel: nothing written, back to the list at the item
        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync("Nicht gespeichert");
        await AllergenCheckbox("A").UncheckAsync();
        await ItemEditor.Locator("#MenuItem\\.Description").FillAsync("Weg damit");
        await Page.GetByTestId("item-savebar").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/Category/{startersId}\\?highlight={soupId}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Confirmation).ToBeHiddenAsync();
        var row = ItemRows.Filter(new() { HasText = "Tomatensuppe" });
        await Assertions.Expect(row).ToHaveAttributeAsync("data-highlighted", "true");
        await Assertions.Expect(row.GetByTestId("allergen-chip")).ToHaveTextAsync(new[] { "A" });
        await using (var db = App.CreateDbContext(ownerId))
        {
            var stored = await db.MenuItems.Include(i => i.Allergens).SingleAsync(i => i.Id == soupId);
            (stored.Name, stored.Description, stored.Allergens.Count).Should().Be(("Tomatensuppe", "Mit Basilikum", 1));
        }

        // Save
        await OpenItemEditorAsync("Tomatensuppe");
        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync("Tomatencremesuppe");
        await ItemEditor.Locator("#MenuItem\\.IsPopular").CheckAsync();
        await SaveItemAndReturnAsync();
        row = ItemRows.Filter(new() { HasText = "Tomatencremesuppe" });
        await Assertions.Expect(row).ToHaveAttributeAsync("data-highlighted", "true");
        await Assertions.Expect(row.Locator("[data-label]")).ToHaveTextAsync(new[] { "Popular" });
        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.SingleAsync(i => i.Id == soupId)).Name.Should().Be("Tomatencremesuppe");
    }

    [Fact]
    public async Task LeavingWithUnsavedChanges_AsksFirst()
    {
        var (ownerId, menuId, startersId, _, soupId) = await SeedAsync();
        await GotoInteractiveAsync($"/MenuItems/item/{soupId}/edit");

        // Without changes the context bar's tabs simply navigate
        await Page.GetByTestId("menu-tabs").GetByRole(AriaRole.Link, new() { Name = "Categories" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/Categories/{menuId}$", RegexOptions.IgnoreCase));

        await GotoInteractiveAsync($"/MenuItems/item/{soupId}/edit");
        // Typed and straight to a link (the field's change event and the navigation leave the browser together)
        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync("Halb fertig");

        // Leaving: the quiet confirmation; "Cancel" stays with the edits
        await Page.GetByTestId("menu-tabs").GetByRole(AriaRole.Link, new() { Name = "Categories" }).ClickAsync();
        await Assertions.Expect(Confirmation).ToBeVisibleAsync();
        await Assertions.Expect(Confirmation.GetByTestId("confirmation-message")).ToContainTextAsync("unsaved changes to “Halb fertig”");
        await Assertions.Expect(Confirmation.Locator("#confirmBtn")).ToHaveTextAsync("Discard changes");
        await Confirmation.Locator("#cancelBtn").ClickAsync();
        await Assertions.Expect(Confirmation).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/item/{soupId}/edit$", RegexOptions.IgnoreCase));
        await Assertions.Expect(ItemEditor.Locator("#MenuItem\\.Name")).ToHaveValueAsync("Halb fertig");
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("Unsaved changes");

        // Escape answers "no" as well
        await Page.GetByTestId("item-edit-back").ClickAsync();
        await Assertions.Expect(Confirmation).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Confirmation).ToBeHiddenAsync();
        await Assertions.Expect(ItemEditor).ToBeVisibleAsync();

        // Discard: leaves, nothing written
        await Page.GetByTestId("item-edit-back").ClickAsync();
        await Confirmation.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/Category/{startersId}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(ItemRows.Filter(new() { HasText = "Tomatensuppe" })).ToHaveCountAsync(1);
        await using var db = App.CreateDbContext(ownerId);
        (await db.MenuItems.SingleAsync(i => i.Id == soupId)).Name.Should().Be("Tomatensuppe");
    }

    [Fact]
    public async Task MovingAnItemToAnotherCategory_AppendsItThere()
    {
        var (ownerId, _, _, mainsId, soupId) = await SeedAsync();
        await GotoInteractiveAsync($"/MenuItems/item/{soupId}/edit");
        await ItemEditor.Locator("#MenuItem\\.CategoryId").SelectOptionAsync(mainsId.ToString());
        await Assertions.Expect(ItemEditor).ToContainTextAsync("Moved items are added at the end of their new category.");
        await SaveItemAndReturnAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/Category/{mainsId}\\?highlight={soupId}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Schnitzel", "Tomatensuppe" });
        await using var db = App.CreateDbContext(ownerId);
        var soup = await db.MenuItems.SingleAsync(i => i.Id == soupId);
        (soup.CategoryId, soup.SortOrder).Should().Be((mainsId, 1));
    }

    [Fact]
    public async Task Validation_KeepsThePage_AndUnknownItemsShowNotFound()
    {
        var (_, _, startersId, _, _) = await SeedAsync();
        await GotoInteractiveAsync($"/MenuItems/Category/{startersId}/new");
        await SaveItemButton.ClickAsync();
        await Assertions.Expect(ItemEditor.GetByText("The Name field is required.")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/new$"));

        await GotoInteractiveAsync($"/MenuItems/item/{Guid.NewGuid()}/edit");
        await Assertions.Expect(Page.GetByTestId("item-not-found")).ToContainTextAsync("Item not found");
    }

    [Fact]
    public async Task Employee_SeesTheItemReadOnly_AndCannotOpenANewOne()
    {
        var (ownerId, _, startersId, _, soupId) = await SeedAsync();
        var employeeEmail = UniqueEmail("employee");
        await App.SeedAsync(ownerId, db =>
        {
            var sub = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(), UserName = employeeEmail, NormalizedUserName = employeeEmail.ToUpperInvariant(),
                Email = employeeEmail, NormalizedEmail = employeeEmail.ToUpperInvariant(), EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(), TenantOwnerId = ownerId.ToString()
            };
            sub.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(sub, Password);
            db.Users.Add(sub);
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(), ApplicationUserId = sub.Id, TenantOwnerId = ownerId.ToString(),
                Role = TenantRole.Employee, DisplayName = "Eva", IsActive = true
            });
            return Task.CompletedTask;
        });
        var (employeeContext, employee) = await NewContextAsync();
        await using var _ = employeeContext;
        await LoginAsync(employee, employeeEmail);

        await GotoInteractiveAsync(employee, $"/MenuItems/item/{soupId}/edit");
        await Assertions.Expect(employee.GetByTestId("item-read-only")).ToBeVisibleAsync();
        await Assertions.Expect(employee.Locator("#MenuItem\\.Name")).ToBeDisabledAsync();
        await Assertions.Expect(employee.Locator("#MenuItem\\.IsVegan")).ToBeDisabledAsync();
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Add variant" })).ToHaveCountAsync(0);

        await GotoInteractiveAsync(employee, $"/MenuItems/Category/{startersId}/new");
        await Assertions.Expect(employee).ToHaveURLAsync(new Regex($"/MenuItems/Category/{startersId}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(employee.GetByTestId("item-edit-form")).ToHaveCountAsync(0);
    }

    private static FilePayload Png(string name, byte r, byte g, byte b)
    {
        using var image = new Image<Rgba32>(320, 240, new Rgba32(r, g, b));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return new FilePayload { Name = name, MimeType = "image/png", Buffer = stream.ToArray() };
    }

    [Fact]
    public async Task Photo_IsUploadedWithSave_AndAPhotoUploadedThenCancelledIsDeletedAgain()
    {
        var (ownerId, _, startersId, _, soupId) = await SeedAsync();
        await GotoInteractiveAsync($"/MenuItems/item/{soupId}/edit");
        var preview = Page.GetByTestId("item-photo-preview").Locator("img");
        await Assertions.Expect(Page.GetByTestId("item-photo-preview")).ToContainTextAsync("No photo yet");

        // Upload and save: the item points at the new image
        await Page.Locator("#MenuItem\\.Image").SetInputFilesAsync(Png("suppe.png", 200, 60, 40));
        await Assertions.Expect(preview).ToHaveAttributeAsync("src", new Regex("^/image/"));
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("Unsaved changes");
        await SaveItemAndReturnAsync();
        string stored;
        await using (var db = App.CreateDbContext(ownerId))
            stored = (await db.MenuItems.SingleAsync(i => i.Id == soupId)).ImagePath!;
        stored.Should().StartWith("image/");
        await Assertions.Expect(ItemRows.Filter(new() { HasText = "Tomatensuppe" }).Locator("img")).ToHaveAttributeAsync("src", "/" + stored);

        // Replace, then cancel: the replacement is deleted, the stored photo stays
        await OpenItemEditorAsync("Tomatensuppe");
        await Assertions.Expect(preview).ToHaveAttributeAsync("src", "/" + stored);
        await Page.Locator("#MenuItem\\.Image").SetInputFilesAsync(Png("neu.png", 20, 120, 60));
        await Assertions.Expect(preview).Not.ToHaveAttributeAsync("src", "/" + stored);
        var replacement = (await preview.GetAttributeAsync("src"))!.TrimStart('/');
        await Page.GetByTestId("item-savebar").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/Category/{startersId}\\?highlight=", RegexOptions.IgnoreCase));
        await using (var db = App.CreateDbContext(ownerId))
        {
            (await db.MenuItems.SingleAsync(i => i.Id == soupId)).ImagePath.Should().Be(stored);
            ImagePaths.TryGetImageId(replacement, out var replacementId).Should().BeTrue();
            ImagePaths.TryGetImageId(stored, out var storedId).Should().BeTrue();
            (await db.Images.AnyAsync(i => i.Id == replacementId)).Should().BeFalse("a photo uploaded and then cancelled is deleted again");
            (await db.Images.AnyAsync(i => i.Id == storedId)).Should().BeTrue();
        }

        // Remove and save: the item has no photo any more
        await OpenItemEditorAsync("Tomatensuppe");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove photo" }).ClickAsync();
        await Assertions.Expect(Page.GetByTestId("item-photo-preview")).ToContainTextAsync("No photo yet");
        await SaveItemAndReturnAsync();
        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.SingleAsync(i => i.Id == soupId)).ImagePath.Should().BeNull();
    }
}
