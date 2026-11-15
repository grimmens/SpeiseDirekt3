using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Search, filter sheet and favourites list ("Merkliste") of the guest view (<c>js/guest-search-filter.js</c>,
/// <c>js/guest-favorites.js</c>). Each test seeds its own small menu (<see cref="SeedMenuAsync"/>) so the counts do not
/// depend on the demo data: two photo categories (cards) and a text-only drinks category (list rows), all labels except
/// halal, allergens A/C/G and price variants on two drinks.
/// </summary>
public class GuestSearchFilterFavoritesTests : E2ETestBase
{
    public GuestSearchFilterFavoritesTests(E2EFixture fixture) : base(fixture) { }

    // A 1 × 1 PNG: enough for a photo category (grid of cards) and the thumbnails
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private sealed record Seeded(Guid MenuId, IReadOnlyDictionary<string, Guid> Items, IReadOnlyDictionary<string, Guid> Options);

    private sealed record Dish(string Category, string Name, string Description, decimal Price, string[] Allergens, bool Photo,
        bool Vegan = false, bool Vegetarian = false, bool Spicy = false, bool AlcoholFree = false, bool Popular = false, bool New = false,
        (string Name, decimal Price)[]? Variants = null);

    private static readonly string[] Categories = { "Salate", "Hauptgerichte", "Getränke" };

    private static readonly Dish[] Dishes =
    {
        new("Salate", "Gartensalat", "Blattsalate, Gurke, Kresse", 8.50m, Array.Empty<string>(), true, Vegan: true),
        new("Salate", "Caesar Salad", "Römersalat, Parmesan, Croûtons", 11.90m, new[] { "A", "C", "G" }, true),
        new("Salate", "Halloumi-Salat", "Gegrillter Halloumi, Minze", 12.40m, new[] { "G" }, false, Vegetarian: true),
        new("Hauptgerichte", "Linsen-Dal", "Rote Linsen, Kokos, Chili", 13.50m, Array.Empty<string>(), true, Vegan: true, Spicy: true),
        new("Hauptgerichte", "Schnitzel", "Vom Kalb, mit Petersilkartoffeln", 18.90m, new[] { "A", "C", "G" }, true, Popular: true),
        new("Hauptgerichte", "Käsespätzle", "Bergkäse, Röstzwiebeln", 14.20m, new[] { "A", "C", "G" }, true, Vegetarian: true, New: true),
        new("Getränke", "Limonade", "Minze und Limette", 3.90m, Array.Empty<string>(), false, Vegan: true, AlcoholFree: true,
            Variants: new[] { ("0,3 l", 3.90m), ("0,5 l", 5.40m) }),
        new("Getränke", "Hausbier", "", 3.40m, new[] { "A" }, false, Variants: new[] { ("0,25 l", 3.40m), ("0,5 l", 5.90m) }),
        new("Getränke", "Espresso", "", 2.90m, Array.Empty<string>(), false, Vegan: true)
    };

    private async Task<Seeded> SeedMenuAsync(string name = "AP4 Testkarte", Guid? ownerId = null)
    {
        var menuId = Guid.NewGuid();
        var items = new Dictionary<string, Guid>();
        var options = new Dictionary<string, Guid>();
        await App.SeedAsync(ownerId ?? E2ESeedData.OwnerId, db =>
        {
            var allergens = new[] { ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("G", "Milch / Laktose") }
                .ToDictionary(a => a.Item1, a => new Allergen { Id = Guid.NewGuid(), Code = a.Item1, Name = a.Item2, MenuId = menuId });
            db.Allergens.AddRange(allergens.Values);
            var categories = Categories.Select((c, i) => new Category { Id = Guid.NewGuid(), Name = c, SortOrder = i, MenuId = menuId }).ToList();
            db.Menus.Add(new Menu { Id = menuId, Name = name, Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German, Categories = categories });

            foreach (var (dish, index) in Dishes.Select((d, i) => (d, i)))
            {
                var item = new MenuItem
                {
                    Id = Guid.NewGuid(), Name = dish.Name, Description = dish.Description, Price = dish.Price, SortOrder = index,
                    CategoryId = categories.Single(c => c.Name == dish.Category).Id,
                    IsVegan = dish.Vegan, IsVegetarian = dish.Vegetarian, IsSpicy = dish.Spicy, IsAlcoholFree = dish.AlcoholFree,
                    IsPopular = dish.Popular, IsNew = dish.New
                };
                foreach (var code in dish.Allergens)
                    item.Allergens.Add(allergens[code]);
                if (dish.Photo)
                {
                    var image = new SpeiseDirekt.Model.Image { Id = Guid.NewGuid(), MimeType = "image/png", Content = TinyPng };
                    db.Images.Add(image);
                    item.ImagePath = $"image/{image.Id}";
                }
                foreach (var (variant, i) in (dish.Variants ?? Array.Empty<(string, decimal)>()).Select((v, i) => (v, i)))
                {
                    var option = new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = variant.Name, Price = variant.Price, SortOrder = i };
                    item.PriceOptions.Add(option);
                    options[$"{dish.Name} {variant.Name}"] = option.Id;
                }
                db.MenuItems.Add(item);
                items[dish.Name] = item.Id;
            }
            return Task.CompletedTask;
        });
        return new Seeded(menuId, items, options);
    }

    private async Task UsePhoneAsync() => await Page.SetViewportSizeAsync(390, 844);

    private ILocator SearchSheet => Page.GetByTestId("search-sheet");
    private ILocator SearchInput => SearchSheet.GetByRole(AriaRole.Searchbox);
    private ILocator VisibleResults => SearchSheet.Locator("[data-search-row]:visible");
    private ILocator FilterSheet => Page.GetByTestId("filter-sheet");
    private ILocator FavoritesSheet => Page.GetByTestId("favorites-sheet");
    private ILocator Section(string name) => Page.Locator($"[data-testid='category-section'][data-category='{name}']");
    private ILocator NavChip(string name) => Page.GetByTestId("category-nav").Locator("[data-category-link]", new() { HasTextRegex = ExactText(name) });
    private ILocator Heart(string itemName) => ItemEntry(itemName).GetByTestId("favorite-button");
    private ILocator VisibleEntries => Page.Locator("main").Locator("[data-testid='item-card']:visible, [data-testid='list-row']:visible");

    private async Task SearchAsync(string query)
    {
        if (!await SearchSheet.IsVisibleAsync())
        {
            await Page.GetByTestId("search-button").ClickAsync();
            await Assertions.Expect(SearchSheet).ToBeVisibleAsync();
        }
        await SearchInput.FillAsync(query);
    }

    private async Task OpenFilterAsync()
    {
        await Page.GetByTestId("filter-button").ClickAsync();
        await Assertions.Expect(FilterSheet).ToBeVisibleAsync();
    }

    private async Task ApplyFilterAsync(string[] labels, string[]? allergens = null)
    {
        await OpenFilterAsync();
        foreach (var key in labels)
            await FilterSheet.GetByTestId($"filter-label-{key}").ClickAsync();
        foreach (var code in allergens ?? Array.Empty<string>())
            await FilterSheet.Locator($"input[data-filter-allergen='{code}']").CheckAsync();
        await FilterSheet.GetByTestId("filter-apply").ClickAsync();
        await Assertions.Expect(FilterSheet).ToBeHiddenAsync();
    }

    private async Task<string[]> VisibleNamesAsync() =>
        (await VisibleEntries.Locator("h3").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray();

    private async Task OpenFavoritesAsync()
    {
        await Page.GetByTestId("favorites-button").ClickAsync();
        await Assertions.Expect(FavoritesSheet).ToBeVisibleAsync();
    }

    private ILocator FavoriteRow(string name) =>
        FavoritesSheet.GetByTestId("favorite-row").Filter(new() { Has = Page.Locator("a", new() { HasTextRegex = ExactText(name) }) });

    private Task<string?> StoredFavoritesAsync(Guid menuId) =>
        Page.EvaluateAsync<string?>($"() => localStorage.getItem('sd-merkliste:{menuId}')");

    // --- Search ---

    [Fact]
    public async Task Search_FindsByNameDescriptionCategoryAndLabel_IgnoringCaseAndAccents()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);

        await SearchAsync("spätzle"); // name
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(1);
        await Assertions.Expect(VisibleResults.First).ToContainTextAsync("Käsespätzle");
        await SearchAsync("KOKOS"); // description, case ignored
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(1);
        await Assertions.Expect(VisibleResults.First).ToContainTextAsync("Linsen-Dal");
        await SearchAsync("getranke"); // category, accents ignored
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(3);
        await Assertions.Expect(SearchSheet.GetByRole(AriaRole.Status)).ToHaveTextAsync("3 Ergebnisse");
        await SearchAsync("croutons"); // û in the description
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(1);
        await Assertions.Expect(VisibleResults.First).ToContainTextAsync("Caesar Salad");

        // Labels: "vegan" finds the vegan items, "vegetarisch" vegan ones too (like the filter)
        await SearchAsync("vegan");
        await Assertions.Expect(VisibleResults.Locator(".gm-search-name"))
            .ToHaveTextAsync(new[] { "Gartensalat", "Linsen-Dal", "Limonade", "Espresso" });
        await SearchAsync("vegetarisch");
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(6);
        await SearchAsync("scharf linsen"); // every word must match
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Search_HighlightsTheMatchedWords_ShowsPriceCategoryAndThumbnail_InMenuOrder()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);

        await SearchAsync("spatzle");
        var row = VisibleResults.First;
        // The original spelling is marked, not the normalized query
        await Assertions.Expect(row.Locator(".gm-search-name mark")).ToHaveTextAsync("spätzle");
        await Assertions.Expect(row.Locator(".gm-search-name")).ToHaveTextAsync("Käsespätzle");
        await Assertions.Expect(row.Locator(".gm-search-category")).ToContainTextAsync("Hauptgerichte");
        await Assertions.Expect(row.GetByTestId("search-price")).ToHaveTextAsync("€ 14,20");
        await Assertions.Expect(row.Locator("img")).ToHaveAttributeAsync("loading", "lazy");

        await SearchAsync("croutons");
        await Assertions.Expect(VisibleResults.First.Locator(".gm-search-desc mark")).ToHaveTextAsync("Croûtons");

        // Several words: every one is marked, in name and category alike
        await SearchAsync("salat");
        await Assertions.Expect(VisibleResults.Locator(".gm-search-name"))
            .ToHaveTextAsync(new[] { "Gartensalat", "Caesar Salad", "Halloumi-Salat" }); // menu order
        var garden = VisibleResults.First;
        await Assertions.Expect(garden.Locator(".gm-search-name mark")).ToHaveTextAsync("salat");
        await Assertions.Expect(garden.Locator(".gm-search-category mark")).ToHaveTextAsync("Salat");
        await Assertions.Expect(garden.Locator(".gm-search-desc mark")).ToHaveTextAsync("salat"); // "Blattsalate"

        // "ab" price for items with differing variants; no thumbnail without a photo
        await SearchAsync("hausbier");
        await Assertions.Expect(VisibleResults.First.GetByTestId("search-price")).ToHaveTextAsync("ab € 3,40");
        await Assertions.Expect(VisibleResults.First.Locator("img")).ToHaveCountAsync(0);

        // The highlight is built from text nodes: menu text never becomes markup
        (await SearchSheet.Locator("mark").EvaluateAllAsync<bool>("ms => ms.every(m => m.children.length === 0)")).Should().BeTrue();
    }

    [Fact]
    public async Task Search_EmptyState_AndOpeningAResult()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);

        await SearchAsync("xyz");
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(0);
        await Assertions.Expect(SearchSheet.GetByRole(AriaRole.Status)).ToHaveTextAsync("Keine Treffer für „xyz“");
        await Assertions.Expect(SearchSheet.GetByTestId("search-empty")).ToBeVisibleAsync();
        await Assertions.Expect(SearchSheet.GetByTestId("search-empty")).ToContainTextAsync("Keine Treffer für „xyz“");

        await SearchAsync("dal");
        await Assertions.Expect(SearchSheet.GetByTestId("search-empty")).ToBeHiddenAsync();
        await VisibleResults.Locator("a").ClickAsync();
        await Assertions.Expect(SearchSheet).ToBeHiddenAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = "Linsen-Dal" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Search_RespectsTheActiveFilter_AndSaysSo()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);
        await ApplyFilterAsync(new[] { "spicy" });

        await SearchAsync("salat");
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(0);
        var note = SearchSheet.GetByTestId("search-filter-note");
        await Assertions.Expect(note).ToBeVisibleAsync();
        await Assertions.Expect(note).ToContainTextAsync("Filter aktiv: 3 weitere Treffer ausgeblendet");

        await note.GetByRole(AriaRole.Button, new() { Name = "Filter zurücksetzen" }).ClickAsync();
        await Assertions.Expect(VisibleResults).ToHaveCountAsync(3);
        await Assertions.Expect(note).ToBeHiddenAsync();
        await Assertions.Expect(SearchInput).ToBeFocusedAsync();
        await Assertions.Expect(Page.GetByTestId("filter-notice")).ToBeHiddenAsync();
    }

    // --- Filter ---

    [Fact]
    public async Task FilterSheet_OffersTheLabelsAndAllergensOfTheMenu_WithALiveCount()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);

        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true });
        await Assertions.Expect(button).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToBeHiddenAsync();
        await button.ClickAsync();
        await Assertions.Expect(FilterSheet).ToHaveAttributeAsync("aria-modal", "true");
        await Assertions.Expect(FilterSheet.GetByRole(AriaRole.Heading, new() { Name = "Filter" })).ToBeVisibleAsync();

        // Only what occurs in the menu (no "Halal"), dietary and highlights separately, in ItemLabels order
        await Assertions.Expect(FilterSheet.Locator("#gm-filter-dietary + .gm-filter-chips [data-filter-label]"))
            .ToHaveTextAsync(new[] { "Vegan", "Vegetarisch", "Alkoholfrei", "Scharf" });
        await Assertions.Expect(FilterSheet.Locator("#gm-filter-highlights + .gm-filter-chips [data-filter-label]"))
            .ToHaveTextAsync(new[] { "Beliebt", "Neu" });
        await Assertions.Expect(FilterSheet.GetByTestId("filter-allergen"))
            .ToHaveTextAsync(new[] { new Regex("A\\s*Glutenhaltiges Getreide"), new Regex("C\\s*Eier"), new Regex("G\\s*Milch / Laktose") });

        var apply = FilterSheet.GetByTestId("filter-apply");
        await Assertions.Expect(apply).ToHaveTextAsync("9 Gerichte anzeigen");
        var vegetarian = FilterSheet.GetByTestId("filter-label-vegetarian");
        await vegetarian.ClickAsync();
        await Assertions.Expect(vegetarian).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(apply).ToHaveTextAsync("6 Gerichte anzeigen"); // vegan items count as vegetarian
        await FilterSheet.GetByTestId("filter-label-spicy").ClickAsync();
        await Assertions.Expect(apply).ToHaveTextAsync("1 Gericht anzeigen");
        await FilterSheet.GetByTestId("filter-reset").ClickAsync();
        await Assertions.Expect(vegetarian).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(apply).ToHaveTextAsync("9 Gerichte anzeigen");

        // Closing without applying discards the draft
        await FilterSheet.GetByTestId("filter-label-vegan").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(FilterSheet).ToBeHiddenAsync();
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(9);
        await Assertions.Expect(button).ToBeFocusedAsync();
        await OpenFilterAsync();
        await Assertions.Expect(FilterSheet.GetByTestId("filter-label-vegan")).ToHaveAttributeAsync("aria-pressed", "false");
    }

    [Fact]
    public async Task FilterByLabel_VegetarianIncludesVegan_TwoLabelsMustBothMatch_EmptySectionsAndChipsHide()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);
        await Assertions.Expect(ItemEntry("Linsen-Dal")).ToHaveAttributeAsync("data-filter-labels", "vegan vegetarian spicy");
        await Assertions.Expect(ItemEntry("Linsen-Dal")).ToHaveAttributeAsync("data-labels", "vegan spicy");

        await ApplyFilterAsync(new[] { "vegetarian" });
        (await VisibleNamesAsync()).Should().Equal("Gartensalat", "Halloumi-Salat", "Linsen-Dal", "Käsespätzle", "Limonade", "Espresso");
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToHaveTextAsync("1");
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Filter (1 aktiv)" })).ToBeVisibleAsync();
        var notice = Page.GetByTestId("filter-notice");
        await Assertions.Expect(notice).ToBeVisibleAsync();
        await Assertions.Expect(notice).ToContainTextAsync("Filter aktiv · 6 Gerichte");

        // Second label: both must match → only the spicy vegan dal; the other sections and their chips disappear
        await OpenFilterAsync();
        await Assertions.Expect(FilterSheet.GetByTestId("filter-label-vegetarian")).ToHaveAttributeAsync("aria-pressed", "true");
        await FilterSheet.GetByTestId("filter-label-spicy").ClickAsync();
        await FilterSheet.GetByTestId("filter-apply").ClickAsync();
        (await VisibleNamesAsync()).Should().Equal("Linsen-Dal");
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToHaveTextAsync("2");
        await Assertions.Expect(notice).ToContainTextAsync("Filter aktiv · 1 Gericht");
        await Assertions.Expect(Section("Salate")).ToBeHiddenAsync();
        await Assertions.Expect(Section("Getränke")).ToBeHiddenAsync();
        await Assertions.Expect(Section("Hauptgerichte")).ToBeVisibleAsync();
        await Assertions.Expect(NavChip("Salate")).ToBeHiddenAsync();
        await Assertions.Expect(NavChip("Getränke")).ToBeHiddenAsync();
        // Scroll-spy: the first visible category is the active one
        await Assertions.Expect(NavChip("Hauptgerichte")).ToHaveAttributeAsync("aria-current", "true");
        // The categories sheet hides them as well
        await Page.GetByTestId("category-all").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("category-sheet").Locator("[data-category-link]:visible")).ToHaveTextAsync(new[] { "Hauptgerichte" });
        await Page.Keyboard.PressAsync("Escape");

        // Reset in the notice shows everything again
        await notice.GetByRole(AriaRole.Button, new() { Name = "Zurücksetzen" }).ClickAsync();
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(9);
        await Assertions.Expect(notice).ToBeHiddenAsync();
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToBeHiddenAsync();
        await Assertions.Expect(NavChip("Salate")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("filter-button")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task FilterByAllergens_ExcludesEveryItemContainingThem()
    {
        var menu = await SeedMenuAsync();
        await OpenMenuAsync(menu.MenuId);

        await ApplyFilterAsync(Array.Empty<string>(), new[] { "G" });
        (await VisibleNamesAsync()).Should().Equal("Gartensalat", "Linsen-Dal", "Limonade", "Hausbier", "Espresso");

        await OpenFilterAsync();
        await FilterSheet.Locator("input[data-filter-allergen='A']").CheckAsync();
        await Assertions.Expect(FilterSheet.GetByTestId("filter-apply")).ToHaveTextAsync("4 Gerichte anzeigen");
        await FilterSheet.GetByTestId("filter-apply").ClickAsync();
        (await VisibleNamesAsync()).Should().Equal("Gartensalat", "Linsen-Dal", "Limonade", "Espresso");
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToHaveTextAsync("2");

        // Labels and allergens together
        await OpenFilterAsync();
        await FilterSheet.GetByTestId("filter-label-alcohol-free").ClickAsync();
        await FilterSheet.GetByTestId("filter-apply").ClickAsync();
        (await VisibleNamesAsync()).Should().Equal("Limonade");
    }

    [Fact]
    public async Task Filter_NothingMatches_ShowsTheEmptyStateWithReset()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);

        await OpenFilterAsync();
        await FilterSheet.GetByTestId("filter-label-vegan").ClickAsync();
        await FilterSheet.GetByTestId("filter-label-popular").ClickAsync();
        await Assertions.Expect(FilterSheet.GetByTestId("filter-apply")).ToHaveTextAsync("0 Gerichte anzeigen");
        await FilterSheet.GetByTestId("filter-apply").ClickAsync();

        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("category-section").First).ToBeHiddenAsync();
        var empty = Page.GetByTestId("filter-empty");
        await Assertions.Expect(empty).ToBeVisibleAsync();
        await Assertions.Expect(empty).ToContainTextAsync("Keine Gerichte passen zu Ihren Filtern.");
        await Assertions.Expect(Page.GetByTestId("filter-notice")).ToContainTextAsync("0 Gerichte");

        await empty.GetByRole(AriaRole.Button, new() { Name = "Filter zurücksetzen" }).ClickAsync();
        await Assertions.Expect(empty).ToBeHiddenAsync();
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(9);
    }

    [Fact]
    public async Task Filter_IsKeptPerMenuInTheSession_ReloadAndBackKeepIt_ANewVisitStartsClean()
    {
        var menu = await SeedMenuAsync();
        var other = await SeedMenuAsync("AP4 Zweitkarte");
        await OpenMenuAsync(menu.MenuId);
        await ApplyFilterAsync(new[] { "vegan" });
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(4);

        await Page.ReloadAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(4);
        await Assertions.Expect(Page.GetByTestId("filter-notice")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToHaveTextAsync("1");

        // Per menu: another menu in the same tab is unfiltered; back brings the filtered one back
        await OpenMenuAsync(other.MenuId);
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(9);
        await Page.GoBackAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/{menu.MenuId}$"));
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(4);

        // A new visit (new browser context = new session) starts clean
        var (context, page) = await NewContextAsync();
        await using (context)
        {
            await OpenGuestPageAsync(page, $"/menu/{menu.MenuId}");
            await Assertions.Expect(page.Locator("main").Locator("[data-testid='item-card']:visible, [data-testid='list-row']:visible")).ToHaveCountAsync(9);
            await Assertions.Expect(page.GetByTestId("filter-notice")).ToBeHiddenAsync();
        }
    }

    [Fact]
    public async Task Filter_StoredUnknownKeysAreIgnored_AndAMenuWithoutLabelsOrAllergensHasNoFilter()
    {
        var menu = await SeedMenuAsync();
        await Context.AddInitScriptAsync($$"""
            sessionStorage.setItem('sd-filter:{{menu.MenuId}}', JSON.stringify({ v: 1, labels: ['halal', 'nope', 'spicy'], allergens: ['X', 'A'] }));
            """);
        await OpenMenuAsync(menu.MenuId);
        (await VisibleNamesAsync()).Should().Equal("Linsen-Dal");
        await Assertions.Expect(Page.GetByTestId("filter-badge")).ToHaveTextAsync("2");

        // The cafe menu of the seed data has neither labels nor allergens
        await OpenMenuAsync(E2ESeedData.MenuWithoutCombosId);
        await Assertions.Expect(Page.GetByTestId("filter-button")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("filter-sheet")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("search-button")).ToBeVisibleAsync();
    }

    // --- Favourites ---

    [Fact]
    public async Task Favorites_FromCardRowAndItemSheet_WithAriaPressedAndFloatingCount()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);
        var fab = Page.GetByTestId("favorites-button");
        await Assertions.Expect(fab).ToBeHiddenAsync();

        // Card
        var card = Heart("Schnitzel");
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-label", "Schnitzel merken");
        await card.ClickAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-label", "Schnitzel von der Merkliste entfernen");
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync(); // the heart does not open the item
        await Assertions.Expect(fab).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");

        // List row
        await Heart("Hausbier").ClickAsync();
        await Assertions.Expect(Heart("Hausbier")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");

        // Item sheet: the heart next to the close button follows the item shown
        await ClickMenuItemAsync("Linsen-Dal");
        var sheetHeart = ItemSheet.GetByTestId("item-favorite-button");
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-label", "Linsen-Dal merken");
        await sheetHeart.ClickAsync();
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-label", "Linsen-Dal von der Merkliste entfernen");
        await CloseItemSheetAsync(Page);
        await Assertions.Expect(Heart("Linsen-Dal")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("3");

        await ClickMenuItemAsync("Schnitzel");
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-pressed", "true");
        await sheetHeart.ClickAsync(); // remove from the sheet
        await CloseItemSheetAsync(Page);
        await Assertions.Expect(Heart("Schnitzel")).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");

        // Deep link: the server-rendered sheet gets its heart too
        await OpenGuestPageAsync(Page, $"/menu/{menu.MenuId}?item={menu.Items["Hausbier"]}");
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(sheetHeart).ToHaveAttributeAsync("aria-label", "Hausbier von der Merkliste entfernen");
    }

    [Fact]
    public async Task FavoritesSheet_ListsInMenuOrder_WithVariantQuantityRemoveAndSum()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);
        // Added in a different order than the menu
        await Heart("Hausbier").ClickAsync();
        await Heart("Schnitzel").ClickAsync();
        await Heart("Linsen-Dal").ClickAsync();

        await OpenFavoritesAsync();
        await Assertions.Expect(FavoritesSheet.GetByRole(AriaRole.Heading, new() { Name = "Merkliste" })).ToBeVisibleAsync();
        await Assertions.Expect(FavoritesSheet.Locator("[data-testid='favorite-row']:visible a"))
            .ToHaveTextAsync(new[] { "Linsen-Dal", "Schnitzel", "Hausbier" });
        await Assertions.Expect(FavoriteRow("Schnitzel")).ToContainTextAsync("Hauptgerichte");
        var sum = FavoritesSheet.GetByTestId("favorites-sum");
        await Assertions.Expect(sum).ToHaveTextAsync("€ 35,80"); // 13,50 + 18,90 + 3,40 (first variant)
        await Assertions.Expect(FavoritesSheet).ToContainTextAsync("Die Merkliste wird nur auf diesem Gerät gespeichert. Bitte bestellen Sie beim Service.");

        // Variant select: the first variant is the default, choosing another changes the sum
        var variant = FavoriteRow("Hausbier").GetByTestId("favorite-variant");
        await Assertions.Expect(variant).ToHaveValueAsync(menu.Options["Hausbier 0,25 l"].ToString());
        await Assertions.Expect(variant.Locator("option")).ToHaveTextAsync(new[] { "0,25 l · € 3,40", "0,5 l · € 5,90" });
        await variant.SelectOptionAsync(menu.Options["Hausbier 0,5 l"].ToString());
        await Assertions.Expect(FavoriteRow("Hausbier").GetByTestId("favorite-line")).ToHaveTextAsync("€ 5,90");
        await Assertions.Expect(sum).ToHaveTextAsync("€ 38,30");
        await Assertions.Expect(FavoriteRow("Schnitzel").GetByTestId("favorite-variant")).ToHaveCountAsync(0);

        // Quantity 1–20
        var schnitzel = FavoriteRow("Schnitzel");
        var minus = schnitzel.GetByRole(AriaRole.Button, new() { Name = "Eine Portion weniger" });
        await Assertions.Expect(minus).ToHaveAttributeAsync("aria-disabled", "true");
        await schnitzel.GetByRole(AriaRole.Button, new() { Name = "Eine Portion mehr" }).ClickAsync();
        await Assertions.Expect(schnitzel.GetByTestId("favorite-qty")).ToHaveTextAsync("2");
        await Assertions.Expect(schnitzel.GetByTestId("favorite-line")).ToHaveTextAsync("€ 37,80");
        await Assertions.Expect(schnitzel.GetByTestId("favorite-unit")).ToHaveTextAsync("2 × € 18,90");
        await Assertions.Expect(FavoriteRow("Hausbier").GetByTestId("favorite-unit")).ToBeHiddenAsync();
        await Assertions.Expect(sum).ToHaveTextAsync("€ 57,20");
        await Assertions.Expect(minus).ToHaveAttributeAsync("aria-disabled", "false");
        var plus = schnitzel.GetByRole(AriaRole.Button, new() { Name = "Eine Portion mehr" });
        for (var i = 0; i < 18; i++)
            await plus.ClickAsync();
        await Assertions.Expect(schnitzel.GetByTestId("favorite-qty")).ToHaveTextAsync("20");
        await Assertions.Expect(plus).ToHaveAttributeAsync("aria-disabled", "true"); // 20 at most
        await Assertions.Expect(schnitzel.GetByTestId("favorite-line")).ToHaveTextAsync("€ 378,00");
        await Assertions.Expect(sum).ToHaveTextAsync("€ 397,40");

        // Remove one row
        await FavoriteRow("Linsen-Dal").GetByRole(AriaRole.Button, new() { Name = "Linsen-Dal von der Merkliste entfernen" }).ClickAsync();
        await Assertions.Expect(FavoritesSheet.Locator("[data-testid='favorite-row']:visible")).ToHaveCountAsync(2);
        await Assertions.Expect(sum).ToHaveTextAsync("€ 383,90");
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Heart("Linsen-Dal")).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");
    }

    [Fact]
    public async Task FavoritesSheet_ClearNeedsAConfirmation()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);
        await Heart("Gartensalat").ClickAsync();
        await Heart("Espresso").ClickAsync();
        await OpenFavoritesAsync();

        var confirm = FavoritesSheet.GetByTestId("favorites-confirm");
        await FavoritesSheet.GetByTestId("favorites-clear").ClickAsync();
        await Assertions.Expect(confirm).ToBeVisibleAsync();
        await Assertions.Expect(confirm).ToContainTextAsync("Merkliste wirklich leeren?");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();
        await Assertions.Expect(confirm).ToBeHiddenAsync();
        await Assertions.Expect(FavoritesSheet.Locator("[data-testid='favorite-row']:visible")).ToHaveCountAsync(2);
        await Assertions.Expect(FavoritesSheet.GetByTestId("favorites-clear")).ToBeFocusedAsync();

        await FavoritesSheet.GetByTestId("favorites-clear").ClickAsync();
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Ja, leeren" }).ClickAsync();
        await Assertions.Expect(FavoritesSheet.Locator("[data-testid='favorite-row']:visible")).ToHaveCountAsync(0);
        await Assertions.Expect(FavoritesSheet.GetByTestId("favorites-empty")).ToBeVisibleAsync();
        await Assertions.Expect(FavoritesSheet.GetByTestId("favorites-sum")).ToBeHiddenAsync();
        (await StoredFavoritesAsync(menu.MenuId)).Should().BeNull();

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Page.GetByTestId("favorites-button")).ToBeHiddenAsync();
        await Assertions.Expect(Heart("Gartensalat")).ToHaveAttributeAsync("aria-pressed", "false");
    }

    [Fact]
    public async Task Favorites_SurviveAReload_PerMenu()
    {
        var menu = await SeedMenuAsync();
        await OpenMenuAsync(menu.MenuId);
        await Heart("Limonade").ClickAsync();
        await Heart("Käsespätzle").ClickAsync();
        await OpenFavoritesAsync();
        await FavoriteRow("Limonade").GetByTestId("favorite-variant").SelectOptionAsync(menu.Options["Limonade 0,5 l"].ToString());
        await FavoriteRow("Limonade").GetByRole(AriaRole.Button, new() { Name = "Eine Portion mehr" }).ClickAsync();

        var stored = JsonDocument.Parse((await StoredFavoritesAsync(menu.MenuId))!).RootElement;
        stored.GetProperty("v").GetInt32().Should().Be(1);
        stored.GetProperty("items").GetArrayLength().Should().Be(2);

        await Page.ReloadAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Heart("Limonade")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");
        await OpenFavoritesAsync();
        await Assertions.Expect(FavoriteRow("Limonade").GetByTestId("favorite-variant")).ToHaveValueAsync(menu.Options["Limonade 0,5 l"].ToString());
        await Assertions.Expect(FavoriteRow("Limonade").GetByTestId("favorite-qty")).ToHaveTextAsync("2");
        await Assertions.Expect(FavoritesSheet.GetByTestId("favorites-sum")).ToHaveTextAsync("€ 25,00"); // 2 × 5,40 + 14,20

        // Another menu has its own (empty) list
        await OpenMenuAsync(E2ESeedData.MezzeMenuId);
        await Assertions.Expect(Page.GetByTestId("favorites-button")).ToBeHiddenAsync();
        await Assertions.Expect(Page.GetByTestId("favorite-button").First).ToHaveAttributeAsync("aria-pressed", "false");
    }

    [Fact]
    public async Task Favorites_UnknownItemsAndVariantsInStorage_AreDroppedSilently()
    {
        var menu = await SeedMenuAsync();
        var stored = JsonSerializer.Serialize(new
        {
            v = 1,
            items = new object[]
            {
                new { id = Guid.NewGuid().ToString(), qty = 3 },                                  // item no longer on the menu
                new { id = menu.Items["Hausbier"].ToString(), variant = Guid.NewGuid().ToString(), qty = 2 }, // variant gone
                new { id = menu.Items["Espresso"].ToString(), qty = 99 }                          // clamped to 20
            }
        });
        await Context.AddInitScriptAsync($"if (!sessionStorage.getItem('seeded')) {{ localStorage.setItem('sd-merkliste:{menu.MenuId}', {JsonSerializer.Serialize(stored)}); sessionStorage.setItem('seeded', '1'); }}");

        await OpenMenuAsync(menu.MenuId);
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");
        await OpenFavoritesAsync();
        await Assertions.Expect(FavoritesSheet.Locator("[data-testid='favorite-row']:visible a")).ToHaveTextAsync(new[] { "Hausbier", "Espresso" });
        await Assertions.Expect(FavoriteRow("Hausbier").GetByTestId("favorite-variant")).ToHaveValueAsync(menu.Options["Hausbier 0,25 l"].ToString());
        await Assertions.Expect(FavoriteRow("Espresso").GetByTestId("favorite-qty")).ToHaveTextAsync("20");
        await Assertions.Expect(FavoritesSheet.GetByTestId("favorites-sum")).ToHaveTextAsync("€ 64,80"); // 2 × 3,40 + 20 × 2,90

        // The next change writes the cleaned list
        await FavoriteRow("Hausbier").GetByRole(AriaRole.Button, new() { Name = "Eine Portion mehr" }).ClickAsync();
        var items = JsonDocument.Parse((await StoredFavoritesAsync(menu.MenuId))!).RootElement.GetProperty("items");
        items.EnumerateArray().Select(i => i.GetProperty("id").GetString()).Should()
            .BeEquivalentTo(new[] { menu.Items["Hausbier"].ToString(), menu.Items["Espresso"].ToString() });
        items.EnumerateArray().Any(i => i.TryGetProperty("variant", out _)).Should().BeFalse("the unknown variant was dropped");

        // Corrupt data does not break the page
        await Page.EvaluateAsync($"() => localStorage.setItem('sd-merkliste:{menu.MenuId}', '{{not json')");
        await Page.ReloadAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.GetByTestId("favorites-button")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task StorageBlocked_PageStillWorks_FavoritesAndFilterInMemory()
    {
        var menu = await SeedMenuAsync();
        await Context.AddInitScriptAsync("""
            for (const name of ['localStorage', 'sessionStorage']) {
                Object.defineProperty(window, name, { configurable: true, get() { throw new DOMException('blocked', 'SecurityError'); } });
            }
            """);
        var errors = new List<string>();
        Page.PageError += (_, e) => errors.Add(e);

        await OpenMenuAsync(menu.MenuId);
        await Heart("Schnitzel").ClickAsync();
        await Assertions.Expect(Heart("Schnitzel")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");
        await OpenFavoritesAsync();
        await Assertions.Expect(FavoritesSheet.GetByTestId("favorites-sum")).ToHaveTextAsync("€ 18,90");
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(FavoritesSheet).ToBeHiddenAsync();

        await ApplyFilterAsync(new[] { "vegan" });
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(4);
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TogglingFavorites_SendsNoRequest()
    {
        var menu = await SeedMenuAsync();
        await OpenMenuAsync(menu.MenuId);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var requests = new List<string>();
        Page.Request += (_, r) => { lock (requests) requests.Add($"{r.Method} {new Uri(r.Url).AbsolutePath}"); };

        await Heart("Schnitzel").ClickAsync();
        await Heart("Hausbier").ClickAsync();
        await Heart("Schnitzel").ClickAsync();
        await Heart("Schnitzel").ClickAsync();
        await OpenFavoritesAsync();
        await FavoriteRow("Hausbier").GetByRole(AriaRole.Button, new() { Name = "Eine Portion mehr" }).ClickAsync();
        await FavoriteRow("Hausbier").GetByTestId("favorite-variant").SelectOptionAsync(menu.Options["Hausbier 0,5 l"].ToString());
        await FavoritesSheet.GetByTestId("favorites-clear").ClickAsync();
        await FavoritesSheet.GetByTestId("favorites-confirm").GetByRole(AriaRole.Button, new() { Name = "Ja, leeren" }).ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Only the lazy thumbnails of the list may load; no tracking, no API
        requests.Where(r => !r.StartsWith("GET /image/", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact]
    public async Task FloatingButton_DoesNotCoverTheLastItem()
    {
        var menu = await SeedMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menu.MenuId);
        await Heart("Espresso").ClickAsync();

        await Page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        var fab = await Page.GetByTestId("favorites-button").BoundingBoxAsync();
        var last = await ItemEntry("Espresso").BoundingBoxAsync();
        (last!.Y + last.Height).Should().BeLessThan(fab!.Y, "the page has room below the last item for the floating button");
        (fab.X + fab.Width).Should().BeLessThanOrEqualTo(390);
    }

    [Fact]
    public async Task OwnerPreviewAndEditorPhoneFrame_FavoritesAndFilterWork()
    {
        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse(db.Users.Single(u => u.Email == email).Id);
        var menu = await SeedMenuAsync(ownerId: ownerId);

        await OpenGuestPageAsync(Page, $"/menu/preview/{menu.MenuId}");
        await Heart("Schnitzel").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");

        // The editor's 380 px phone frame shows the same page (same origin and menu: same list); tall enough window
        // so the whole frame is on screen
        await Page.SetViewportSizeAsync(1280, 1400);
        await GotoInteractiveAsync($"/menu/edit/{menu.MenuId}");
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        await frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Assertions.Expect(frame.GetByTestId("favorites-count")).ToHaveTextAsync("1");
        var beer = frame.Locator("[data-testid='list-row']", new() { Has = frame.Locator("h3", new() { HasTextRegex = ExactText("Hausbier") }) });
        await beer.GetByTestId("favorite-button").ClickAsync();
        await Assertions.Expect(frame.GetByTestId("favorites-count")).ToHaveTextAsync("2");
        await frame.GetByTestId("favorites-button").ClickAsync();
        await Assertions.Expect(frame.GetByTestId("favorites-sum")).ToHaveTextAsync("€ 22,30");
        await frame.GetByTestId("favorites-sheet").GetByRole(AriaRole.Button, new() { Name = "Schließen" }).ClickAsync();

        await frame.GetByTestId("filter-button").ClickAsync();
        await frame.GetByTestId("filter-label-vegan").ClickAsync();
        await frame.GetByTestId("filter-apply").ClickAsync();
        await Assertions.Expect(frame.Locator("main [data-testid='item-card']:visible, main [data-testid='list-row']:visible")).ToHaveCountAsync(4);
        await Assertions.Expect(frame.GetByTestId("filter-notice")).ToBeVisibleAsync();
    }
}
