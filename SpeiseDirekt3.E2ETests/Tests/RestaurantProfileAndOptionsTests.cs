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
/// Back office for the guest-menu data model (package AP1): the restaurant profile on <c>/restaurant</c>, category
/// subtitles, the item labels from <see cref="ItemLabels"/>, price variants (the base price follows the lowest one)
/// and extras, and deleting items and menus that have price options.
/// </summary>
public class RestaurantProfileAndOptionsTests : BackOfficeTestBase
{
    public RestaurantProfileAndOptionsTests(E2EFixture fixture) : base(fixture) { }

    private ILocator Editor => ItemEditor;
    private ILocator ItemRows => Page.GetByTestId("menu-item-row");
    private ILocator SaveProfile => Page.GetByRole(AriaRole.Button, new() { Name = "Save profile" });
    private ILocator ExactButton(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private async Task<Guid> OwnerIdAsync(string email)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
    }

    /// <summary>A small PNG of one colour (the upload resizes it, so the content only has to be a valid image).</summary>
    private static byte[] Png(byte r, byte g, byte b, int width = 300, int height = 100)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(r, g, b));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Waiting for an upload (circuit, ImageSharp resize, database): 2–5 s under parallel load measured in AP12b, past the
    /// 5 s default in a busy run (the "Profile_IsSaved… under load" flake of AP12a), so uploads get the 15 s of the
    /// context's action timeout. The assertions themselves are unchanged.
    /// </summary>
    private static readonly LocatorAssertionsToHaveAttributeOptions Upload = new() { Timeout = 15_000 };

    private static FilePayload PngFile(string name, byte r, byte g, byte b) =>
        new() { Name = name, MimeType = "image/png", Buffer = Png(r, g, b) };

    // --- Restaurant profile ---

    [Fact]
    public async Task Profile_IsSaved_WithPictures_ReloadsWithItsValues_AndReplacedPicturesAreDeleted()
    {
        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);

        // Top navigation entry next to Menus / QR codes
        await GotoInteractiveAsync("/dashboard");
        await Page.GetByTestId("top-nav").GetByRole(AriaRole.Link, new() { Name = "Restaurant", Exact = true }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/restaurant$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Restaurant profile" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("profile-not-saved")).ToBeVisibleAsync();

        await Page.Locator("#Restaurant\\.Name").FillAsync("Gasthaus Zur Post");
        await Page.Locator("#Restaurant\\.Description").FillAsync("Wirtshaus seit 1890");
        await Page.Locator("#Restaurant\\.Phone").FillAsync("+43 1 234 56 78");
        await Page.Locator("#Restaurant\\.Email").FillAsync("info@example.com");
        await Page.Locator("#Restaurant\\.Website").FillAsync("https://example.com");
        await Page.Locator("#Restaurant\\.Address").FillAsync("Hauptplatz 1\n1010 Wien");
        await Page.Locator("#Restaurant\\.OpeningHours").FillAsync("Mo–Fr 11:00–22:00\nSa 10:00–23:00");
        await Page.Locator("#Restaurant\\.InstagramUrl").FillAsync("https://example.com/instagram");
        await Page.Locator("#Restaurant\\.FacebookUrl").FillAsync("https://example.com/facebook");
        await Page.Locator("#Restaurant\\.GoogleReviewUrl").FillAsync("https://example.com/review");

        await Page.Locator("#Restaurant\\.Cover").SetInputFilesAsync(PngFile("cover.png", 200, 80, 40));
        await Assertions.Expect(Page.GetByTestId("cover-preview").Locator("img")).ToHaveAttributeAsync("src", new Regex("^/image/"), Upload);
        await Page.Locator("#Restaurant\\.Logo").SetInputFilesAsync(PngFile("logo.png", 20, 60, 160));
        await Assertions.Expect(Page.GetByTestId("logo-preview").Locator("img")).ToHaveAttributeAsync("src", new Regex("^/image/"), Upload);

        await SaveProfile.ClickAsync();
        await Assertions.Expect(Page.GetByTestId("profile-saved")).ToContainTextAsync("Profile saved");
        await Assertions.Expect(Page.GetByTestId("profile-not-saved")).ToHaveCountAsync(0);

        RestaurantProfile stored;
        await using (var db = App.CreateDbContext(ownerId))
            stored = await db.RestaurantProfiles.AsNoTracking().SingleAsync();
        stored.ApplicationUserId.Should().Be(ownerId);
        stored.Name.Should().Be("Gasthaus Zur Post");
        stored.Address.Should().Be("Hauptplatz 1\n1010 Wien");
        stored.OpeningHours.Should().Be("Mo–Fr 11:00–22:00\nSa 10:00–23:00");
        stored.LogoImagePath.Should().StartWith("image/");
        stored.CoverImagePath.Should().StartWith("image/").And.NotBe(stored.LogoImagePath);

        // A fresh page load shows the stored values and pictures
        await GotoInteractiveAsync("/restaurant");
        await Assertions.Expect(Page.Locator("#Restaurant\\.Name")).ToHaveValueAsync("Gasthaus Zur Post");
        await Assertions.Expect(Page.Locator("#Restaurant\\.OpeningHours")).ToHaveValueAsync("Mo–Fr 11:00–22:00\nSa 10:00–23:00");
        await Assertions.Expect(Page.Locator("#Restaurant\\.GoogleReviewUrl")).ToHaveValueAsync("https://example.com/review");
        await Assertions.Expect(Page.GetByTestId("logo-preview").Locator("img")).ToHaveAttributeAsync("src", "/" + stored.LogoImagePath);
        await Assertions.Expect(Page.GetByTestId("cover-preview").Locator("img")).ToHaveAttributeAsync("src", "/" + stored.CoverImagePath);
        var logoResponse = await Page.APIRequest.GetAsync("/" + stored.LogoImagePath);
        logoResponse.Status.Should().Be(200);

        // Replace the logo and remove the cover: after saving, both old pictures are gone from the database
        await Page.Locator("#Restaurant\\.Logo").SetInputFilesAsync(PngFile("logo2.png", 10, 140, 60));
        await Assertions.Expect(Page.GetByTestId("logo-preview").Locator("img")).Not.ToHaveAttributeAsync("src", "/" + stored.LogoImagePath, Upload);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove cover image" }).ClickAsync();
        await Assertions.Expect(Page.GetByTestId("cover-preview")).ToContainTextAsync("No cover image yet");
        await SaveProfile.ClickAsync();
        await Assertions.Expect(Page.GetByTestId("profile-saved")).ToBeVisibleAsync();

        await using (var db = App.CreateDbContext(ownerId))
        {
            var updated = await db.RestaurantProfiles.AsNoTracking().SingleAsync();
            updated.CoverImagePath.Should().BeNull();
            updated.LogoImagePath.Should().StartWith("image/").And.NotBe(stored.LogoImagePath);
            ImagePaths.TryGetImageId(stored.LogoImagePath, out var oldLogo).Should().BeTrue();
            ImagePaths.TryGetImageId(stored.CoverImagePath, out var oldCover).Should().BeTrue();
            ImagePaths.TryGetImageId(updated.LogoImagePath, out var newLogo).Should().BeTrue();
            (await db.Images.AnyAsync(i => i.Id == oldLogo || i.Id == oldCover)).Should().BeFalse();
            (await db.Images.AnyAsync(i => i.Id == newLogo)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Profile_InvalidValues_ShowMessages_AndNothingIsSaved()
    {
        var email = await RegisterNewOwnerAsync();
        await GotoInteractiveAsync("/restaurant");

        await Page.Locator("#Restaurant\\.Website").FillAsync("example.com");
        await Page.Locator("#Restaurant\\.InstagramUrl").FillAsync("instagram.com/zurpost");
        await Page.Locator("#Restaurant\\.Email").FillAsync("kein-mail");
        await SaveProfile.ClickAsync();

        const string urlMessage = "Enter a full web address starting with https:// (or http://).";
        await Assertions.Expect(Page.GetByText("Name is required.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Enter a valid e-mail address.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText(urlMessage)).ToHaveCountAsync(2);
        await Assertions.Expect(Page.GetByTestId("profile-invalid")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("profile-saved")).ToHaveCountAsync(0);

        var ownerId = await OwnerIdAsync(email);
        await using (var db = App.CreateDbContext(ownerId))
            (await db.RestaurantProfiles.AnyAsync()).Should().BeFalse();

        // Corrected (an http:// address is fine too, an empty field is optional): saved
        await Page.Locator("#Restaurant\\.Name").FillAsync("Zur Post");
        await Page.Locator("#Restaurant\\.Website").FillAsync("http://example.com");
        await Page.Locator("#Restaurant\\.InstagramUrl").FillAsync("");
        await Page.Locator("#Restaurant\\.Email").FillAsync("info@example.com");
        await SaveProfile.ClickAsync();
        await Assertions.Expect(Page.GetByTestId("profile-saved")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText(urlMessage)).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByText("Name is required.")).ToHaveCountAsync(0);

        await using (var db = App.CreateDbContext(ownerId))
        {
            var profile = await db.RestaurantProfiles.SingleAsync();
            profile.Website.Should().Be("http://example.com");
            profile.InstagramUrl.Should().BeNull();
        }
    }

    [Fact]
    public async Task Profile_IsReadOnly_ForAnEmployee_AndCannotBeSaved()
    {
        var ownerEmail = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(ownerEmail);
        var employeeEmail = UniqueEmail("employee");
        await App.SeedAsync(ownerId, db =>
        {
            db.RestaurantProfiles.Add(new RestaurantProfile { Id = Guid.NewGuid(), Name = "Lokal des Chefs", Phone = "0123" });
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

        await LogoutAsync(Page);
        await LoginAsync(Page, employeeEmail);
        await GotoInteractiveAsync("/restaurant");

        // The owner's profile, read-only: no save button, disabled fields
        await Assertions.Expect(Page.Locator("#Restaurant\\.Name")).ToHaveValueAsync("Lokal des Chefs");
        await Assertions.Expect(Page.Locator("#Restaurant\\.Name")).ToBeDisabledAsync();
        await Assertions.Expect(Page.Locator("#Restaurant\\.Logo")).ToBeDisabledAsync();
        await Assertions.Expect(Page.GetByTestId("profile-read-only")).ToBeVisibleAsync();
        await Assertions.Expect(SaveProfile).ToHaveCountAsync(0);
    }

    // --- Category subtitle ---

    [Fact]
    public async Task CategorySubtitle_CanBeAdded_Edited_AndRemoved()
    {
        var email = await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Untertitel Menü");

        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Page.Locator("#Input\\.Name").FillAsync("Mezze");
        await Page.Locator("#Input\\.Description").FillAsync("  Kalt und warm  ");
        await Button("Add Category").ClickAsync();
        var row = Page.GetByTestId("category-row").Filter(new() { HasText = "Mezze" });
        await Assertions.Expect(row.GetByTestId("category-description")).ToHaveTextAsync("Kalt und warm");
        // The add form is empty again
        await Assertions.Expect(Page.Locator("#Input\\.Description")).ToHaveValueAsync("");

        // Edited in the row (AP11: inline instead of a modal)
        var form = Page.GetByTestId("category-edit");
        async Task EditAsync(string subtitle, string button)
        {
            await row.GetByRole(AriaRole.Button, new() { Name = "Edit Mezze", Exact = true }).ClickAsync();
            await Assertions.Expect(form).ToBeVisibleAsync();
            await form.Locator("#Category\\.Description").FillAsync(subtitle);
            await form.GetByRole(AriaRole.Button, new() { Name = button, Exact = true }).ClickAsync();
            await Assertions.Expect(form).ToHaveCountAsync(0);
        }

        await EditAsync("Bis 22:00 Uhr", "Save");
        await Assertions.Expect(row.GetByTestId("category-description")).ToHaveTextAsync("Bis 22:00 Uhr");
        await EditAsync("Nicht speichern", "Cancel");
        await Assertions.Expect(row.GetByTestId("category-description")).ToHaveTextAsync("Bis 22:00 Uhr");

        var ownerId = await OwnerIdAsync(email);
        await using (var db = App.CreateDbContext(ownerId))
            (await db.Categories.SingleAsync(c => c.MenuId == menuId)).Description.Should().Be("Bis 22:00 Uhr");

        await EditAsync("", "Save");
        await Assertions.Expect(row.GetByTestId("category-description")).ToHaveCountAsync(0);
        await using (var db = App.CreateDbContext(ownerId))
            (await db.Categories.SingleAsync(c => c.MenuId == menuId)).Description.Should().BeNull();
    }

    // --- Labels ---

    [Fact]
    public async Task NewLabels_AreSaved_ShownAsChipsInDisplayOrder_AndTickedWhenEditing()
    {
        var email = await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Label Menü");
        await CreateCategoryViaUiAsync(menuId, "Mezze");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Link("New Menu Entry").ClickAsync();
        await Assertions.Expect(Editor).ToBeVisibleAsync();
        // Every label of the central list has a checkbox, in display order
        await Assertions.Expect(Editor.GetByTestId("item-label-options").Locator("input[type=checkbox]"))
            .ToHaveCountAsync(ItemLabels.All.Count);
        await Editor.Locator("#MenuItem\\.Name").FillAsync("Falafel");
        await Editor.Locator("#MenuItem\\.Price").FillAsync("9.80");
        foreach (var property in new[] { "IsHalal", "IsAlcoholFree", "IsNew", "IsPopular", "IsVegan", "IsVegetarian" })
            await Editor.Locator($"#MenuItem\\.{property}").CheckAsync();
        await SaveItemAndReturnAsync();

        // Chips in the order of ItemLabels; vegan hides vegetarian
        var row = ItemRows.Filter(new() { HasText = "Falafel" });
        await Assertions.Expect(row.Locator("[data-label]")).ToHaveTextAsync(new[] { "Popular", "New", "Vegan", "Halal", "Alcohol-free" });

        await using (var db = App.CreateDbContext(await OwnerIdAsync(email)))
        {
            var item = await db.MenuItems.SingleAsync(i => i.Name == "Falafel");
            (item.IsHalal, item.IsAlcoholFree, item.IsNew, item.IsPopular, item.IsVegan, item.IsVegetarian, item.IsSpicy)
                .Should().Be((true, true, true, true, true, true, false));
        }

        await OpenItemEditorAsync("Falafel");
        await Assertions.Expect(Editor.Locator("#MenuItem\\.IsHalal")).ToBeCheckedAsync();
        await Assertions.Expect(Editor.Locator("#MenuItem\\.IsVegetarian")).ToBeCheckedAsync();
        await Assertions.Expect(Editor.Locator("#MenuItem\\.IsSpicy")).Not.ToBeCheckedAsync();
    }

    // --- Price variants ---

    [Fact]
    public async Task Variants_SetTheBasePriceToTheLowest_CanBeReordered_AndRemoved()
    {
        var email = await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Varianten Menü");
        await CreateCategoryViaUiAsync(menuId, "Getränke");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Link("New Menu Entry").ClickAsync();
        await Editor.Locator("#MenuItem\\.Name").FillAsync("Hausbier");
        await Editor.Locator("#MenuItem\\.Price").FillAsync("4.20");

        var basePrice = Editor.Locator("#MenuItem\\.Price");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Add variant" }).ClickAsync();
        // The new row's name field gets the focus; the first variant starts at the current price
        await Assertions.Expect(Editor.GetByLabel("Name of variant 1")).ToBeFocusedAsync();
        await Assertions.Expect(Editor.GetByLabel("Price of variant 1")).ToHaveValueAsync("4.20");
        await Editor.GetByLabel("Name of variant 1").FillAsync("0,5 l");
        await Editor.GetByLabel("Price of variant 1").FillAsync("5.90");
        await Editor.GetByLabel("Price of variant 1").PressAsync("Tab");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Add variant" }).ClickAsync();
        await Editor.GetByLabel("Name of variant 2").FillAsync("0,25 l");
        await Editor.GetByLabel("Price of variant 2").FillAsync("3.40");
        await Editor.GetByLabel("Price of variant 2").PressAsync("Tab");

        // The base price is read-only and follows the lowest variant
        await Assertions.Expect(basePrice).ToHaveValueAsync("3.40");
        await Assertions.Expect(basePrice).ToHaveAttributeAsync("readonly", "");
        await Assertions.Expect(Editor.GetByTestId("price-follows-variants")).ToContainTextAsync("from €3.40");

        // Smallest size first: the variant's grip handle, keyboard path (↑); at the top it stays
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Reorder variant 0,25 l" }).FocusAsync();
        await Page.Keyboard.PressAsync("ArrowUp");
        var variantNames = Editor.GetByTestId("variant-row").Locator("input[aria-label^='Name of']");
        await Assertions.Expect(variantNames.Nth(0)).ToHaveValueAsync("0,25 l");
        await Assertions.Expect(variantNames.Nth(1)).ToHaveValueAsync("0,5 l");
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(Page)).ToHaveTextAsync("variant 0,25 l is already first.");
        await Assertions.Expect(variantNames.Nth(0)).ToHaveValueAsync("0,25 l");

        await SaveItemAndReturnAsync();

        var row = ItemRows.Filter(new() { HasText = "Hausbier" });
        await Assertions.Expect(row.GetByTestId("item-price")).ToHaveTextAsync("from €3.40");
        await Assertions.Expect(row.GetByTestId("item-price-options")).ToHaveTextAsync("2 variants");

        var ownerId = await OwnerIdAsync(email);
        await using (var db = App.CreateDbContext(ownerId))
        {
            var item = await db.MenuItems.Include(i => i.PriceOptions).SingleAsync(i => i.Name == "Hausbier");
            item.Price.Should().Be(3.40m);
            item.Variants().Select(o => (o.Name, o.Price, o.SortOrder)).Should().Equal(("0,25 l", 3.40m, 0), ("0,5 l", 5.90m, 1));
            item.PriceOptions.Should().OnlyContain(o => o.ApplicationUserId == ownerId);
        }

        // Change the cheaper variant's price, then remove it: the base price follows each time
        await OpenItemEditorAsync("Hausbier");
        await Assertions.Expect(Editor.GetByLabel("Name of variant 1")).ToHaveValueAsync("0,25 l");
        await Editor.GetByLabel("Price of variant 1").FillAsync("6.10");
        await Editor.GetByLabel("Price of variant 1").PressAsync("Tab");
        await Assertions.Expect(basePrice).ToHaveValueAsync("5.90");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Remove variant 0,5 l" }).ClickAsync();
        await Assertions.Expect(basePrice).ToHaveValueAsync("6.10");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Remove variant 0,25 l" }).ClickAsync();
        await Assertions.Expect(Editor.GetByTestId("variant-row")).ToHaveCountAsync(0);

        // No variants left: the price is editable again
        await Assertions.Expect(basePrice).Not.ToHaveAttributeAsync("readonly", "");
        await basePrice.FillAsync("4.50");
        await SaveItemAndReturnAsync();
        await Assertions.Expect(row.GetByTestId("item-price")).ToHaveTextAsync("€4.50");
        await Assertions.Expect(row.GetByTestId("item-price-options")).ToHaveCountAsync(0);

        await using (var db = App.CreateDbContext(ownerId))
        {
            var item = await db.MenuItems.Include(i => i.PriceOptions).SingleAsync(i => i.Name == "Hausbier");
            item.Price.Should().Be(4.50m);
            item.PriceOptions.Should().BeEmpty();
            (await db.MenuItemPriceOptions.IgnoreQueryFilters().AnyAsync(o => o.MenuItemId == item.Id)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task VariantRow_WithoutName_IsRejected_WithAMessageAtTheRow()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Leere Variante");
        await CreateCategoryViaUiAsync(menuId, "Getränke");

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Link("New Menu Entry").ClickAsync();
        await Editor.Locator("#MenuItem\\.Name").FillAsync("Saft");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Add variant" }).ClickAsync();
        await Editor.GetByLabel("Price of variant 1").FillAsync("3.20");
        await SaveItemButton.ClickAsync();

        await Assertions.Expect(Editor).ToBeVisibleAsync();
        await Assertions.Expect(Editor.GetByTestId("variant-row").GetByText("Name is required.")).ToBeVisibleAsync();

        await Editor.GetByLabel("Name of variant 1").FillAsync("0,2 l");
        await SaveItemAndReturnAsync();
        await Assertions.Expect(ItemRows.Filter(new() { HasText = "Saft" }).GetByTestId("item-price")).ToHaveTextAsync("from €3.20");
    }

    // --- Extras ---

    [Fact]
    public async Task Extras_AreSaved_KeepThePrice_AndCancelDiscardsUnsavedOptions()
    {
        var email = await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Extras Menü");
        await CreateCategoryViaUiAsync(menuId, "Salate");
        await CreateMenuItemViaUiAsync(menuId, "Fattoush", "Brotsalat", 11.90m);

        await OpenItemEditorAsync("Fattoush");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Add extra" }).ClickAsync();
        await Editor.GetByLabel("Name of extra 1").FillAsync("+ Halloumi");
        await Editor.GetByLabel("Price of extra 1").FillAsync("6.00");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Add extra" }).ClickAsync();
        await Editor.GetByLabel("Name of extra 2").FillAsync("+ Hähnchen");
        await Editor.GetByLabel("Price of extra 2").FillAsync("5.00");
        await Editor.GetByLabel("Price of extra 2").PressAsync("Tab");
        // Extras do not touch the base price
        await Assertions.Expect(Editor.Locator("#MenuItem\\.Price")).ToHaveValueAsync("11.90");
        await Assertions.Expect(Editor.Locator("#MenuItem\\.Price")).Not.ToHaveAttributeAsync("readonly", "");
        await SaveItemAndReturnAsync();

        var row = ItemRows.Filter(new() { HasText = "Fattoush" });
        await Assertions.Expect(row.GetByTestId("item-price")).ToHaveTextAsync("€11.90");
        await Assertions.Expect(row.GetByTestId("item-price-options")).ToHaveTextAsync("2 extras");

        // Unsaved changes are dropped by Cancel, also for a later save of something else (reordering)
        await OpenItemEditorAsync("Fattoush");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Remove extra + Halloumi" }).ClickAsync();
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Add variant" }).ClickAsync();
        await Editor.GetByLabel("Name of variant 1").FillAsync("Groß");
        await Editor.Locator("#MenuItem\\.Name").FillAsync("Nicht gespeichert");
        await Editor.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(Editor).ToHaveCountAsync(0);
        await Assertions.Expect(row.GetByTestId("item-price-options")).ToHaveTextAsync("2 extras");

        await CreateMenuItemViaUiAsync(menuId, "Tabouleh", "Petersiliensalat", 9.50m);
        await ExactButton("Reorder Tabouleh").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(ItemRows).ToContainTextAsync(new[] { "Tabouleh", "Fattoush" });

        await using var db = App.CreateDbContext(await OwnerIdAsync(email));
        var item = await db.MenuItems.Include(i => i.PriceOptions).SingleAsync(i => i.Name == "Fattoush");
        item.Price.Should().Be(11.90m);
        item.Extras().Select(o => (o.Name, o.Price)).Should().Equal(("+ Halloumi", 6.00m), ("+ Hähnchen", 5.00m));
        item.Variants().Should().BeEmpty();
        (await db.MenuItems.AnyAsync(i => i.Name == "Nicht gespeichert")).Should().BeFalse();
    }

    // --- Deleting with price options ---

    [Fact]
    public async Task ItemsAndMenus_WithPriceOptions_CanBeDeleted()
    {
        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);
        var menuId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        MenuItem WithOptions(string name) => new()
        {
            Id = Guid.NewGuid(), Name = name, Description = "", Price = 3.40m, CategoryId = categoryId,
            PriceOptions =
            {
                new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,25 l", Price = 3.40m, SortOrder = 0 },
                new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = "0,5 l", Price = 5.90m, SortOrder = 1 },
                new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Extra, Name = "+ Zitrone", Price = 0.30m, SortOrder = 0 }
            }
        };
        var single = WithOptions("Radler");
        var kept = WithOptions("Hausbier");
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = "Optionen Menü", Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German,
                Categories = new List<Category> { new() { Id = categoryId, Name = "Getränke", MenuId = menuId } }
            });
            db.MenuItems.AddRange(single, kept);
            db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = "s1", MenuId = menuId, MenuItemId = single.Id });
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Assertions.Expect(ItemRows.Filter(new() { HasText = "Radler" }).GetByTestId("item-price-options")).ToHaveTextAsync("2 variants · 1 extra");
        var confirmation = Page.Locator("#confirmationModal");
        await ExactButton("Delete Radler").ClickAsync();
        await confirmation.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(ItemRows).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByText("could not be deleted")).ToHaveCountAsync(0);

        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItemPriceOptions.IgnoreQueryFilters().CountAsync(o => o.MenuItemId == single.Id)).Should().Be(0);

        // The whole menu (category → item → options cascade)
        await GotoInteractiveAsync("/menus");
        await Page.GetByTestId("menu-card").Filter(new() { Has = Page.Locator("h3", new() { HasTextString = "Optionen Menü" }) })
            .GetByRole(AriaRole.Button, new() { Name = "Delete menu Optionen Menü" }).ClickAsync();
        await confirmation.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("menu-card").Filter(new() { HasText = "Optionen Menü" })).ToHaveCountAsync(0);

        await using (var db = App.CreateDbContext(ownerId))
        {
            (await db.Menus.AnyAsync(m => m.Id == menuId)).Should().BeFalse();
            (await db.MenuItemPriceOptions.IgnoreQueryFilters().CountAsync(o => o.MenuItemId == kept.Id)).Should().Be(0);
        }
    }

    // --- Dashboard ---

    [Fact]
    public async Task Dashboard_SuggestsTheProfile_AsAnOptionalStep_UntilOneExists()
    {
        var email = await RegisterNewOwnerAsync();
        await GotoInteractiveAsync("/dashboard");

        // Not one of the four counted steps
        await Assertions.Expect(Page.GetByTestId("checklist-step")).ToHaveCountAsync(4);
        await Assertions.Expect(Page.GetByTestId("getting-started-progress")).ToHaveTextAsync("0 of 4 done");
        var optional = Page.GetByTestId("checklist-optional");
        await Assertions.Expect(optional).ToContainTextAsync("Add your restaurant profile");
        await Assertions.Expect(optional.GetByTestId("checklist-optional-action")).ToHaveAttributeAsync("href", "restaurant");

        await App.SeedAsync(await OwnerIdAsync(email), db =>
        {
            db.RestaurantProfiles.Add(new RestaurantProfile { Id = Guid.NewGuid(), Name = "Schon da" });
            return Task.CompletedTask;
        });
        await GotoInteractiveAsync("/dashboard");
        await Assertions.Expect(Page.GetByTestId("checklist-step")).ToHaveCountAsync(4);
        await Assertions.Expect(optional).ToHaveCountAsync(0);
    }
}
