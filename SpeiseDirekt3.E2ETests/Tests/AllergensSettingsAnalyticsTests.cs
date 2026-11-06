using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Back-office usability package P4: the allergen quick-fill (14 EU allergens with the Austrian letter codes),
/// usage counts and the delete confirmation on <c>/allergens/{menuId}</c>, the phone-sized preview on the menu
/// settings page, the analytics layout (summary tiles first, one "Sessions" series) and the role tooltips on
/// <c>/users</c>.
/// </summary>
public class AllergensSettingsAnalyticsTests : BackOfficeTestBase
{
    public AllergensSettingsAnalyticsTests(E2EFixture fixture) : base(fixture) { }

    private static readonly IReadOnlyDictionary<string, string> GermanNames = new Dictionary<string, string>
    {
        ["A"] = "Glutenhaltiges Getreide", ["B"] = "Krebstiere", ["C"] = "Eier", ["D"] = "Fisch",
        ["E"] = "Erdnüsse", ["F"] = "Soja", ["G"] = "Milch / Laktose", ["H"] = "Schalenfrüchte",
        ["L"] = "Sellerie", ["M"] = "Senf", ["N"] = "Sesam", ["O"] = "Sulfite", ["P"] = "Lupinen", ["R"] = "Weichtiere"
    };

    private ILocator QuickFill => Page.GetByTestId("eu-quick-fill");
    private ILocator QuickFillNotice => Page.GetByTestId("eu-quick-fill-notice");
    private ILocator QuickFillHint => Page.GetByTestId("eu-quick-fill-hint");
    private ILocator AllergenRows => Page.Locator("table tbody tr");
    private ILocator ConfirmationModal => Page.Locator("#confirmationModal");

    /// <summary>The row of the allergen with exactly this code (case-sensitive).</summary>
    private ILocator AllergenRow(string code) =>
        Page.Locator("table tbody tr", new() { Has = Page.Locator("td", new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(code)}\\s*$") }) });

    // --- Seeding helpers ---

    private async Task<Guid> RegisterOwnerAsync()
    {
        var email = await RegisterNewOwnerAsync();
        await using var db = App.CreateDbContext(Guid.Empty);
        return Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
    }

    private async Task<Guid> SeedMenuAsync(Guid ownerId, string name, MenuLanguage language = MenuLanguage.German,
        Action<ApplicationDbContext, Guid>? more = null)
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.Add(new Menu { Id = menuId, Name = name, Description = "P4", Language = language, Theme = DesignTheme.Classic });
            more?.Invoke(db, menuId);
            return Task.CompletedTask;
        });
        return menuId;
    }

    /// <summary>
    /// A menu with one category and the allergens A (used by two items), C (one item) and L (unused).
    /// Returns the menu id and the category id.
    /// </summary>
    private async Task<(Guid MenuId, Guid CategoryId)> SeedMenuWithAllergenUsageAsync(Guid ownerId)
    {
        var categoryId = Guid.NewGuid();
        var menuId = await SeedMenuAsync(ownerId, "Nutzung", more: (db, id) =>
        {
            var a = new Allergen { Id = Guid.NewGuid(), MenuId = id, Code = "A", Name = "Glutenhaltiges Getreide" };
            var c = new Allergen { Id = Guid.NewGuid(), MenuId = id, Code = "C", Name = "Eier" };
            var l = new Allergen { Id = Guid.NewGuid(), MenuId = id, Code = "L", Name = "Sellerie" };
            db.Allergens.AddRange(a, c, l);
            db.Categories.Add(new Category { Id = categoryId, Name = "Hauptspeisen", MenuId = id });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Schnitzel", Description = "Paniert", Price = 19.90m, CategoryId = categoryId, Allergens = { a, c } });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Nudeln", Description = "Hausgemacht", Price = 12.50m, CategoryId = categoryId, Allergens = { a } });
        });
        return (menuId, categoryId);
    }

    /// <summary>A sub-account of the tenant (the role legend on /users only shows once there is one).</summary>
    private Task SeedSubAccountAsync(Guid ownerId, string displayName, TenantRole role) =>
        App.SeedAsync(ownerId, db =>
        {
            var subId = Guid.NewGuid().ToString();
            var email = UniqueEmail("team");
            db.Users.Add(new ApplicationUser
            {
                Id = subId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
                NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
                TenantOwnerId = ownerId.ToString()
            });
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(), ApplicationUserId = subId, TenantOwnerId = ownerId.ToString(),
                Role = role, DisplayName = displayName, IsActive = true
            });
            return Task.CompletedTask;
        });

    private async Task<List<(string Code, string Name)>> AllergensInDbAsync(Guid ownerId, Guid menuId)
    {
        await using var db = App.CreateDbContext(ownerId);
        var rows = await db.Allergens.Where(a => a.MenuId == menuId).Select(a => new { a.Code, a.Name }).ToListAsync();
        return rows.Select(a => (a.Code, a.Name)).ToList();
    }

    private async Task OpenRowActionAsync(string code, string action)
    {
        await AllergenRow(code).Locator("button").First.ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = action }).ClickAsync();
    }

    // --- Allergen quick-fill ---

    [Fact]
    public async Task QuickFill_OnGermanMenu_AddsThe14AllergensWithGermanNames_ThenIsDisabled()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Gasthaus P4");

        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(QuickFillHint).ToContainTextAsync("German names");
        await Assertions.Expect(QuickFill).ToBeEnabledAsync();
        await QuickFill.ClickAsync();

        await Assertions.Expect(QuickFillNotice).ToHaveTextAsync(new Regex(@"^\s*14 allergens added, 0 already existed\.\s*$"));
        await Assertions.Expect(AllergenRows).ToHaveCountAsync(14);
        foreach (var (code, name) in GermanNames)
            await Assertions.Expect(AllergenRow(code)).ToContainTextAsync(name);

        // Nothing left to add: disabled, and the hint says why
        await Assertions.Expect(QuickFill).ToBeDisabledAsync();
        await Assertions.Expect(QuickFillHint).ToHaveTextAsync(new Regex("All 14 EU allergens are already on this menu"));

        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(QuickFill).ToBeDisabledAsync();
        var stored = await AllergensInDbAsync(ownerId, menuId);
        stored.Select(a => a.Code).Should().BeEquivalentTo(GermanNames.Keys);
        stored.Should().AllSatisfy(a => a.Name.Should().Be(GermanNames[a.Code]));
    }

    [Fact]
    public async Task QuickFill_KeepsAnExistingCode_AndAddsThe13Others()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Menü mit A");
        await CreateAllergenViaUiAsync(menuId, "A", "Gluten (eigene Bezeichnung)");

        await QuickFill.ClickAsync();

        await Assertions.Expect(QuickFillNotice).ToHaveTextAsync(new Regex(@"13 allergens added, 1 already existed\."));
        await Assertions.Expect(AllergenRows).ToHaveCountAsync(14);
        await Assertions.Expect(AllergenRow("A")).ToContainTextAsync("Gluten (eigene Bezeichnung)");
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Glutenhaltiges Getreide" })).ToHaveCountAsync(0);
        await Assertions.Expect(AllergenRow("B")).ToContainTextAsync("Krebstiere");
    }

    [Fact]
    public async Task QuickFill_OnEnglishMenu_UsesEnglishNames_AndMatchesCodesCaseInsensitively()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Pub P4", MenuLanguage.English, (db, id) =>
            db.Allergens.Add(new Allergen { Id = Guid.NewGuid(), MenuId = id, Code = "g", Name = "Dairy" }));

        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(QuickFillHint).ToContainTextAsync("English names");
        await QuickFill.ClickAsync();

        await Assertions.Expect(QuickFillNotice).ToHaveTextAsync(new Regex(@"13 allergens added, 1 already existed\."));
        await Assertions.Expect(AllergenRows).ToHaveCountAsync(14);
        await Assertions.Expect(AllergenRow("A")).ToContainTextAsync("Cereals containing gluten");
        await Assertions.Expect(AllergenRow("O")).ToContainTextAsync("Sulphites");
        await Assertions.Expect(AllergenRow("R")).ToContainTextAsync("Molluscs");
        // "g" counts as G: kept as it is, no second milk allergen
        await Assertions.Expect(AllergenRow("g")).ToContainTextAsync("Dairy");
        await Assertions.Expect(AllergenRow("G")).ToHaveCountAsync(0);
        await Assertions.Expect(QuickFill).ToBeDisabledAsync();
    }

    [Fact]
    public async Task QuickFill_WhenEverythingExistsMeanwhile_AddsNothingAndSaysSo()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Zweiter Klick");
        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(QuickFill).ToBeEnabledAsync();

        // Someone else fills the list while the page is open (another tab, the API)
        await App.SeedAsync(ownerId, db =>
        {
            foreach (var (code, name) in GermanNames)
                db.Allergens.Add(new Allergen { Id = Guid.NewGuid(), MenuId = menuId, Code = code, Name = name });
            return Task.CompletedTask;
        });

        await QuickFill.ClickAsync();

        await Assertions.Expect(QuickFillNotice).ToHaveTextAsync(new Regex("No allergens added: all 14 EU allergens already existed"));
        await Assertions.Expect(AllergenRows).ToHaveCountAsync(14);
        await Assertions.Expect(QuickFill).ToBeDisabledAsync();
        (await AllergensInDbAsync(ownerId, menuId)).Should().HaveCount(14);
    }

    [Fact]
    public async Task QuickFill_IsNotOfferedWithoutCreatePermission()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Nur lesen");
        await CreateAllergenViaUiAsync(menuId, "A", "Gluten");

        var employeeEmail = UniqueEmail("employee");
        await GotoInteractiveAsync("/users/invite");
        await Page.GetByPlaceholder("employee@example.com").FillAsync(employeeEmail);
        await Page.GetByPlaceholder("John Doe").FillAsync("Erika Lesend");
        await Page.GetByPlaceholder("Min. 8 characters").FillAsync(Password);
        await Page.Locator("select").SelectOptionAsync(new SelectOptionValue { Label = "Employee" });
        await Button("Create Account").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));

        var (employeeContext, employee) = await NewContextAsync();
        await using var _ = employeeContext;
        await LoginAsync(employee, employeeEmail);
        await GotoInteractiveAsync(employee, $"/allergens/{menuId}");

        await Assertions.Expect(employee.Locator("table td", new() { HasTextString = "Gluten" })).ToBeVisibleAsync();
        await Assertions.Expect(employee.GetByTestId("eu-quick-fill")).ToHaveCountAsync(0);
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Add Allergen" })).ToHaveCountAsync(0);
    }

    // --- Allergen list: links, usage, delete ---

    [Fact]
    public async Task AllergensPage_HasCompactLinksInsteadOfNavigationCards()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Links P4");

        await GotoInteractiveAsync($"/allergens/{menuId}");

        var links = Page.GetByTestId("allergen-related-links");
        await Assertions.Expect(links.GetByRole(AriaRole.Link, new() { Name = "Categories" })).ToHaveAttributeAsync("href", $"/Categories/{menuId}");
        await Assertions.Expect(links.GetByRole(AriaRole.Link, new() { Name = "Menu items" })).ToHaveAttributeAsync("href", $"/MenuItems/{menuId}");
        await Assertions.Expect(links.GetByRole(AriaRole.Link, new() { Name = "Combos" })).ToHaveAttributeAsync("href", $"/combos/{menuId}");
        await Assertions.Expect(Page.GetByText("Manage Categories")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByText("Manage Menu Items")).ToHaveCountAsync(0);

        await links.GetByRole(AriaRole.Link, new() { Name = "Combos" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/combos/{menuId}$"));
    }

    [Fact]
    public async Task AllergenList_ShowsHowManyItemsUseEachAllergen()
    {
        var ownerId = await RegisterOwnerAsync();
        var (menuId, categoryId) = await SeedMenuWithAllergenUsageAsync(ownerId);

        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(AllergenRow("A").GetByTestId("allergen-usage")).ToHaveTextAsync(new Regex(@"^\s*2 items\s*$"));
        await Assertions.Expect(AllergenRow("C").GetByTestId("allergen-usage")).ToHaveTextAsync(new Regex(@"^\s*1 item\s*$"));
        await Assertions.Expect(AllergenRow("L").GetByTestId("allergen-usage")).ToHaveTextAsync(new Regex(@"^\s*Not used\s*$"));

        // An item with L appears: the count follows
        await App.SeedAsync(ownerId, async db =>
        {
            var celery = await db.Allergens.SingleAsync(a => a.MenuId == menuId && a.Code == "L");
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Suppe", Description = "Klar", Price = 4.90m, CategoryId = categoryId, Allergens = { celery } });
        });
        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(AllergenRow("L").GetByTestId("allergen-usage")).ToHaveTextAsync(new Regex(@"^\s*1 item\s*$"));
    }

    [Fact]
    public async Task DeletingAnAllergenInUse_ConfirmationNamesTheItemCount()
    {
        var ownerId = await RegisterOwnerAsync();
        var (menuId, _) = await SeedMenuWithAllergenUsageAsync(ownerId);
        await GotoInteractiveAsync($"/allergens/{menuId}");

        // In use: the question names the count; aborting keeps it
        await OpenRowActionAsync("A", "Delete");
        await Assertions.Expect(ConfirmationModal).ToBeVisibleAsync();
        await Assertions.Expect(ConfirmationModal.Locator(".modal-body")).ToContainTextAsync("is used by 2 items");
        await ConfirmationModal.Locator("#cancelBtn").ClickAsync();
        await Assertions.Expect(ConfirmationModal).ToBeHiddenAsync();
        await Assertions.Expect(AllergenRows).ToHaveCountAsync(3);

        // Not in use: the plain question
        await OpenRowActionAsync("L", "Delete");
        await Assertions.Expect(ConfirmationModal.Locator(".modal-body")).ToContainTextAsync("Are you sure you want to delete the allergen \"Sellerie\"?");
        await Assertions.Expect(ConfirmationModal.Locator(".modal-body")).Not.ToContainTextAsync("used by");
        await ConfirmationModal.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(AllergenRow("L")).ToHaveCountAsync(0);

        // Confirmed: the allergen goes, the items stay (without it)
        await OpenRowActionAsync("C", "Delete");
        await Assertions.Expect(ConfirmationModal.Locator(".modal-body")).ToContainTextAsync("is used by 1 item.");
        await ConfirmationModal.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(AllergenRow("C")).ToHaveCountAsync(0);
        await Assertions.Expect(AllergenRows).ToHaveCountAsync(1);

        await using var db = App.CreateDbContext(ownerId);
        var schnitzel = await db.MenuItems.Include(i => i.Allergens).SingleAsync(i => i.Name == "Schnitzel" && i.Category!.MenuId == menuId);
        schnitzel.Allergens.Select(a => a.Code).Should().BeEquivalentTo(new[] { "A" });
    }

    // --- Menu settings preview ---

    [Fact]
    public async Task MenuSettingsPreview_IsAPhoneSizedFrameWithItsOwnScrollbar()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Dreizehn Gerichte", more: (db, id) =>
        {
            var categories = new[] { "Vorspeisen", "Hauptspeisen", "Desserts", "Getränke" }
                .Select(n => new Category { Id = Guid.NewGuid(), Name = n, MenuId = id }).ToArray();
            db.Categories.AddRange(categories);
            for (var i = 1; i <= 13; i++)
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = $"Gericht {i}", Description = "Beschreibung", Price = 10m + i, CategoryId = categories[i % 4].Id });
        });

        await GotoInteractiveAsync($"/menu/edit/{menuId}"); // 1280 x 900

        var frame = Page.GetByTestId("menu-preview-frame");
        await Assertions.Expect(frame.Locator("h3", new() { HasTextRegex = new Regex(@"^\s*Gericht 13\s*$") })).ToHaveCountAsync(1);
        var box = await frame.BoundingBoxAsync();
        box.Should().NotBeNull();
        box!.Height.Should().BeInRange(500, 700);
        box.Width.Should().BeLessThanOrEqualTo(400);

        // The menu scrolls inside the frame, not the page
        var scroll = Page.GetByTestId("menu-preview-scroll");
        var heights = await scroll.EvaluateAsync<int[]>("e => [e.scrollHeight, e.clientHeight]");
        heights[0].Should().BeGreaterThan(heights[1] + 200);
        await scroll.EvaluateAsync("e => { e.scrollTop = e.scrollHeight; }");
        (await scroll.EvaluateAsync<int>("e => Math.round(e.scrollTop)")).Should().BeGreaterThan(0);
        (await Page.EvaluateAsync<int>("() => Math.round(window.scrollY)")).Should().Be(0);

        // The theme arrows still switch the theme; the preview stays rendered
        var theme = Page.Locator("#themeSelect");
        await Assertions.Expect(theme).ToHaveValueAsync("Classic");
        await Page.Locator("#themeSelect + button").ClickAsync();
        await Assertions.Expect(theme).ToHaveValueAsync("Minimal");
        await Page.Locator("button:has(+ #themeSelect)").ClickAsync();
        await Page.Locator("button:has(+ #themeSelect)").ClickAsync();
        await Assertions.Expect(theme).ToHaveValueAsync("Modern");
        await Assertions.Expect(frame.Locator("h3", new() { HasTextRegex = new Regex(@"^\s*Gericht 1\s*$") })).ToHaveCountAsync(1);
    }

    // --- Analytics ---

    [Fact]
    public async Task Analytics_ShowsTheSummaryTilesAboveTheCharts_AndSessionsInsteadOfUsers()
    {
        var ownerId = await RegisterOwnerAsync();
        await SeedMenuAsync(ownerId, "Analytics P4", more: (db, menuId) =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId };
            var item = new MenuItem { Id = Guid.NewGuid(), Name = "Gulasch", Description = "Mit Knödel", Price = 12m, CategoryId = category.Id };
            db.Categories.Add(category);
            db.MenuItems.Add(item);
            // 5 views from 4 sessions, 3 clicks from two of them; all within the last 24 hours (UTC, like the service)
            var t = DateTime.UtcNow.AddHours(-2);
            foreach (var session in new[] { "s1", "s1", "s2", "s3", "s4" })
                db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = $"{session}-{menuId:N}", MenuId = menuId, ViewedAt = t });
            foreach (var session in new[] { "s1", "s2", "s2" })
                db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = $"{session}-{menuId:N}", MenuId = menuId, MenuItemId = item.Id, ClickedAt = t });
        });

        await GotoInteractiveAsync("/analytics");

        var summary = Page.GetByTestId("analytics-summary");
        await Assertions.Expect(summary.GetByTestId("summary-views").GetByTestId("summary-value")).ToHaveTextAsync("5");
        await Assertions.Expect(summary.GetByTestId("summary-clicks").GetByTestId("summary-value")).ToHaveTextAsync("3");
        await Assertions.Expect(summary.GetByTestId("summary-sessions").GetByTestId("summary-value")).ToHaveTextAsync("4");

        // Tiles first, then the three charts
        var tilesBeforeCharts = await Page.EvaluateAsync<bool>("""
            () => {
                const tiles = document.querySelector('[data-testid=analytics-summary]');
                const charts = [...document.querySelectorAll('canvas')];
                return charts.length === 3 && charts.every(c => tiles.compareDocumentPosition(c) & Node.DOCUMENT_POSITION_FOLLOWING);
            }
            """);
        tilesBeforeCharts.Should().BeTrue();

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sessions", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Traffic per User")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByText("Unique Users")).ToHaveCountAsync(0);

        // The time range buttons still reload the numbers
        await Button("Last 24 Hours").ClickAsync();
        await Assertions.Expect(summary.GetByTestId("summary-views").GetByTestId("summary-value")).ToHaveTextAsync("5");
        await Assertions.Expect(Button("Last 7 Days")).ToBeVisibleAsync();
        await Assertions.Expect(Button("Last 30 Days")).ToBeVisibleAsync();
    }

    // --- Users: role legend ---

    [Fact]
    public async Task UsersRoleChips_ExplainTheRolesDefaultPermissions()
    {
        var ownerId = await RegisterOwnerAsync();
        await SeedSubAccountAsync(ownerId, "Max Kellner", TenantRole.Employee);

        await GotoInteractiveAsync("/users");
        var legend = Page.GetByTestId("role-legend");
        await Assertions.Expect(legend).ToBeVisibleAsync();

        async Task<string> TitleOf(TenantRole role) => (await legend.Locator($"[data-role={role}]").GetAttributeAsync("title"))!;

        var employee = await TitleOf(TenantRole.Employee);
        employee.Should().Contain("Allergens: view").And.NotContain("Allergens: view, create").And.NotContain("Analytics");
        (await TitleOf(TenantRole.Manager)).Should().Contain("Team: view, invite, edit").And.NotContain("remove");
        (await TitleOf(TenantRole.Cashier)).Should().Contain("Menu items: view, edit");
        (await TitleOf(TenantRole.Driver)).Should().Contain("Delivery: view, manage").And.NotContain("Allergens");
        (await TitleOf(TenantRole.GeneralManager)).Should().Contain("Subscription: manage");
    }

    // --- Screenshots for the design review (opt-in, like AdminScreenshotTour) ---

    [Fact]
    public async Task Capture_package_pages_for_review()
    {
        if (Environment.GetEnvironmentVariable(AdminScreenshotTour.DirectoryEnvVar) is not { Length: > 0 } dir) return;
        System.IO.Directory.CreateDirectory(dir);

        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Gasthaus Zur Post", more: (db, id) =>
        {
            var allergens = new[] { ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("G", "Milch / Laktose"), ("L", "Sellerie") }
                .ToDictionary(a => a.Item1, a => new Allergen { Id = Guid.NewGuid(), MenuId = id, Code = a.Item1, Name = a.Item2 });
            db.Allergens.AddRange(allergens.Values);
            var categories = new[] { "Vorspeisen", "Hauptspeisen", "Desserts", "Getränke" }
                .Select(n => new Category { Id = Guid.NewGuid(), Name = n, MenuId = id }).ToArray();
            db.Categories.AddRange(categories);
            var dishes = new[]
            {
                ("Frittatensuppe", "A,C,L"), ("Kürbiscremesuppe", "G,L"), ("Bruschetta", "A"), ("Wiener Schnitzel vom Kalb", "A,C,G"),
                ("Zwiebelrostbraten", "A"), ("Käsespätzle", "A,C,G"), ("Chili sin Carne", ""), ("Tagesfisch", "G"),
                ("Apfelstrudel", "A,C,G"), ("Kaiserschmarrn", "A,C,G"), ("Sommerspritzer 0,25 l", ""), ("Almdudler 0,35 l", ""), ("Melange", "G")
            };
            for (var i = 0; i < dishes.Length; i++)
            {
                var item = new MenuItem { Id = Guid.NewGuid(), Name = dishes[i].Item1, Description = "Hausgemacht", Price = 4.5m + i, CategoryId = categories[i * 4 / dishes.Length].Id };
                foreach (var code in dishes[i].Item2.Split(',', StringSplitOptions.RemoveEmptyEntries)) item.Allergens.Add(allergens[code]);
                db.MenuItems.Add(item);
            }
        });

        await SeedSubAccountAsync(ownerId, "Max Kellner", TenantRole.Employee);

        async Task Shot(IPage page, string name, bool fullPage = true)
        {
            try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5_000 }); } catch (TimeoutException) { }
            await page.WaitForTimeoutAsync(700);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"p4-{name}.png"), FullPage = fullPage });
        }

        // Desktop
        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Shot(Page, "desktop-allergens");
        await QuickFill.ClickAsync();
        await Assertions.Expect(QuickFillNotice).ToBeVisibleAsync();
        await Shot(Page, "desktop-allergens-filled");
        await OpenRowActionAsync("A", "Delete");
        await Assertions.Expect(ConfirmationModal).ToBeVisibleAsync();
        await Shot(Page, "desktop-allergens-delete-confirm", fullPage: false);
        await ConfirmationModal.Locator("#cancelBtn").ClickAsync();

        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        await Shot(Page, "desktop-menu-edit");
        await Page.GetByTestId("menu-preview-frame").Locator("h3", new() { HasTextString = "Bruschetta" }).ClickAsync();
        await Shot(Page, "desktop-menu-edit-overlay", fullPage: false);

        // Phone: same cookies, narrow viewport
        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();
        await GotoInteractiveAsync(phone, $"/allergens/{menuId}");
        await Shot(phone, "phone-allergens");
        await GotoInteractiveAsync(phone, $"/menu/edit/{menuId}");
        await Shot(phone, "phone-menu-edit");
        await GotoInteractiveAsync(phone, "/users");
        await Shot(phone, "phone-users");
    }
}
