using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP12b: the back-office areas in a German browser (<see cref="E2ESettings.GermanLocale"/>) — menus, items, allergens,
/// combos, QR codes with the FullCalendar month view, analytics with Chart.js, restaurant profile and team — with German
/// counts (one/other forms), prices ("ab 4,20 €"), confirmations, model validation messages, calendar and chart texts.
/// The English pages are covered by the area tests, which run in an en-US browser.
/// </summary>
public class LocalizedAreasTests : QrCalendarTestBase
{
    public LocalizedAreasTests(E2EFixture fixture) : base(fixture) { }

    protected override string ContextLocale => E2ESettings.GermanLocale;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    /// <summary>"4,20 €" with the no-break space of <c>UiFormat.Price</c>.</summary>
    private static string Euro(string amount) => amount + " €";

    [Fact]
    public async Task MenuItemsAllergensAndCombos_InGerman_WithGermanCountsAndPrices()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await GuestDemoData.SeedAsync(App, ownerId, DesignTheme.Classic, name: "Trattoria");

        // Menus list: heading, design and language names, counts
        await GotoInteractiveAsync("/menus");
        await Assertions.Expect(Page).ToHaveTitleAsync("Speisekarten - Speise-Direkt");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Ihre Speisekarten" })).ToBeVisibleAsync();
        var card = Page.GetByTestId("menu-card").First;
        await Assertions.Expect(card).ToContainTextAsync("Klassisch");
        await Assertions.Expect(card).ToContainTextAsync("Deutsch");
        await Assertions.Expect(card.GetByTestId("menu-card-counts")).ToContainTextAsync("7 Kategorien");
        await Assertions.Expect(card.GetByTestId("menu-card-counts")).ToContainTextAsync("0 QR-Codes");

        // The delete confirmation names the action in German, next to "Abbrechen"
        await card.Locator("button[aria-label^='Speisekarte ']").ClickAsync();
        var confirmation = Page.Locator("#confirmationModal");
        await Assertions.Expect(confirmation.Locator("#confirmation-title")).ToHaveTextAsync("Speisekarte löschen");
        await Assertions.Expect(confirmation.GetByTestId("confirmation-message")).ToHaveTextAsync("„Trattoria“ mit allen Kategorien und Gerichten löschen?");
        await Assertions.Expect(confirmation.Locator("#confirmBtn")).ToHaveTextAsync("Speisekarte löschen");
        await Assertions.Expect(confirmation.Locator("#cancelBtn")).ToHaveTextAsync("Abbrechen");
        await confirmation.Locator("#cancelBtn").ClickAsync();
        await Assertions.Expect(confirmation).ToBeHiddenAsync();

        // Items: German prices with variants and extras, label names like the guest chips
        await GotoInteractiveAsync($"/MenuItems/Category/{menu.CategoryIds["Alkoholfreie Getränke"]}");
        var lemonade = Page.GetByTestId("menu-item-row").Filter(new() { HasText = GuestDemoData.LemonadeName });
        await Assertions.Expect(lemonade.GetByTestId("item-price")).ToHaveTextAsync("ab " + Euro("4,20"));
        await Assertions.Expect(lemonade.GetByTestId("item-price-options")).ToHaveTextAsync("2 Varianten");
        await Assertions.Expect(lemonade.GetByTestId("item-labels")).ToContainTextAsync("Alkoholfrei");
        await Assertions.Expect(Page.GetByTestId("menu-item-count")).ToHaveTextAsync("3 Gerichte");
        await GotoInteractiveAsync($"/MenuItems/Category/{menu.CategoryIds["Pizza"]}");
        var margherita = Page.GetByTestId("menu-item-row").Filter(new() { HasText = GuestDemoData.DishWithExtras });
        await Assertions.Expect(margherita.GetByTestId("item-price")).ToHaveTextAsync(Euro("10,50"));
        await Assertions.Expect(margherita.GetByTestId("item-price-options")).ToHaveTextAsync("3 Extras");

        // The item page: German sections and the German leave confirmation
        await GotoInteractiveAsync($"/MenuItems/item/{menu.Items[GuestDemoData.DishWithExtras]}/edit");
        await Assertions.Expect(Page.GetByTestId("item-section-price")).ToContainTextAsync("Preis und Optionen");
        await Assertions.Expect(Page.GetByTestId("item-label-options")).ToContainTextAsync("Vegetarisch");
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("Noch keine Änderungen");
        await Page.Locator("#MenuItem\\.Name").FillAsync("Margherita speciale");
        await Page.Locator("#MenuItem\\.Name").BlurAsync(); // InputText reports on change
        await Assertions.Expect(Page.GetByTestId("item-dirty-state")).ToHaveTextAsync("Ungespeicherte Änderungen");
        await Page.GetByTestId("menu-tabs").Locator("[data-tab='categories']").ClickAsync();
        await Assertions.Expect(confirmation.GetByTestId("confirmation-message")).ToContainTextAsync("ungespeicherte Änderungen an „Margherita speciale“");
        await Assertions.Expect(confirmation.Locator("#confirmBtn")).ToHaveTextAsync("Änderungen verwerfen");
        await confirmation.Locator("#cancelBtn").ClickAsync();

        // Allergens with usage counts; combos with German counts and prices
        await GotoInteractiveAsync($"/allergens/{menu.MenuId}");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Allergene", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("allergen-usage").First).ToHaveTextAsync(new Regex(@"^\s*(1 Gericht|\d+ Gerichte|Nicht verwendet)\s*$"));
        await GotoInteractiveAsync($"/combos/{menu.MenuId}");
        var combo = Page.GetByTestId("combo-card").First;
        await Assertions.Expect(combo).ToContainTextAsync("Enthält (2 Gerichte)");
        await Assertions.Expect(combo).ToContainTextAsync(Euro("19,90"));
        await Assertions.Expect(combo).ToContainTextAsync("Auslösendes Gericht");
    }

    [Fact]
    public async Task QrCalendar_InGerman_MonthNamesButtonsEntriesAndValidation()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Weihnachten");
        var fallbackId = await SeedMenuAsync(ownerId, "Hauptkarte");
        var year = DateTime.Today.Year + 1;
        var qrId = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Festtage", IsCalendarBased = true, MenuId = fallbackId,
            CalendarEntries =
            [
                Calendar(new DateOnly(year, 12, 24), menuId, until: new DateOnly(year, 12, 26)),
                Calendar(new DateOnly(year, 12, 4), menuId, weekly: DayOfWeek.Friday)
            ]
        });

        // The list: mode badge and target in German
        await GotoInteractiveAsync("/qrcodes");
        var card = Page.GetByTestId("qr-card").Filter(new() { HasText = "Festtage" });
        await Assertions.Expect(card.GetByTestId("qr-mode")).ToHaveTextAsync("Kalender");
        await Assertions.Expect(card.GetByTestId("qr-target")).ToHaveTextAsync("2 Einträge, sonst Hauptkarte");

        // The editor: FullCalendar in German, Monday first; entries in words; the legend
        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");
        await WaitForCalendarAsync(Page);
        await GotoMonthAsync(year, 12);
        var calendar = CalendarOf(Page);
        await Assertions.Expect(calendar.Locator(".fc-toolbar-title")).ToHaveTextAsync($"Dezember {year}");
        await Assertions.Expect(calendar.Locator(".fc-today-button")).ToHaveTextAsync("Heute");
        await Assertions.Expect(calendar.Locator(".fc-dayGridMonth-button")).ToHaveTextAsync("Monat");
        await Assertions.Expect(calendar.Locator(".fc-listMonth-button")).ToHaveTextAsync("Liste");
        await Assertions.Expect(calendar.Locator(".fc-prev-button")).ToHaveAttributeAsync("title", "Vorheriger Monat");
        await Assertions.Expect(calendar.Locator(".fc-col-header-cell").First).ToHaveTextAsync(new Regex(@"^\s*Mo\.?\s*$"));
        string Date(int month, int day) => new DateOnly(year, month, day).ToString("ddd, d. MMM yyyy", German);
        await Assertions.Expect(EntryRow($"Täglich vom {Date(12, 24)} bis {Date(12, 26)}")).ToHaveCountAsync(1);
        await Assertions.Expect(EntryRow($"Jeden Freitag ab {Date(12, 4)}")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByTestId("calendar-fallback-hint")).ToHaveTextAsync("Andere Tage: Hauptkarte (Standard-Speisekarte)");
        await calendar.Locator(".fc-listMonth-button").ClickAsync();
        // The list view: German day headings and the (screen-reader) column headers; no times (displayEventTime: false)
        await Assertions.Expect(calendar.Locator(".fc-list").First).ToContainTextAsync($"24. Dezember {year}");
        await Assertions.Expect(calendar.Locator(".fc-list thead").First).ToContainTextAsync("Uhrzeit");

        // A new calendar code without entries: the model's message in German
        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync("Leer");
        await SelectCalendarModeAsync(); // click, then wait for the server's re-render (CheckAsync expects it at once)
        await Page.GetByRole(AriaRole.Button, new() { Name = "QR-Code erstellen", Exact = true }).ClickAsync();
        await Assertions.Expect(Page.GetByText("Es muss mindestens einen Eintrag im Kalender geben.").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AnalyticsRestaurantAndTeam_InGerman_WithChartTextsAndFormatsInGerman()
    {
        await RegisterOwnerAsync();

        await GotoInteractiveAsync("/analytics");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Statistik", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Zeitraum" }).GetByRole(AriaRole.Button))
            .ToHaveTextAsync(["Letzte 24 Stunden", "Letzte 7 Tage", "Letzte 30 Tage", "Diagramme aktualisieren"]);
        await Assertions.Expect(Page.GetByTestId("summary-views")).ToContainTextAsync("Kartenaufrufe");
        // Chart.js: titles from C#, numbers and dates through Intl in German
        await Page.WaitForFunctionAsync("() => window.Chart && Chart.getChart('menuTrafficChart') && Chart.getChart('userTrafficChart')");
        (await Page.EvaluateAsync<string>("() => Chart.getChart('menuTrafficChart').options.plugins.title.text"))
            .Should().Be("Kartenaufrufe im Verlauf – keine Daten");
        (await Page.EvaluateAsync<string>("() => Chart.getChart('menuTrafficChart').options.locale")).Should().Be("de");
        (await Page.EvaluateAsync<string>("() => Chart.getChart('userTrafficChart').options.scales.x.ticks.callback(new Date(2026, 10, 18).getTime())"))
            .Should().Be("18. Nov.");
        (await Page.EvaluateAsync<string>("() => Chart.getChart('userTrafficChart').options.scales.x.title.text")).Should().Be("Datum");

        await GotoInteractiveAsync("/restaurant");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Restaurantprofil", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#Restaurant\\.OpeningHours")).ToHaveAttributeAsync("placeholder", "Mo–Fr 11:00–22:00\nSa 10:00–23:00\nSo Ruhetag");
        await Page.Locator("#Restaurant\\.Website").FillAsync("beispiel.de");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Profil speichern", Exact = true }).ClickAsync();
        await Assertions.Expect(Page.GetByText("Geben Sie eine vollständige Webadresse ein, die mit https:// (oder http://) beginnt.")).ToBeVisibleAsync();

        await GotoInteractiveAsync("/users/invite");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Neues Teammitglied einladen" })).ToBeVisibleAsync();
        // AP14: no role picker — the new member is a manager, one sentence says what that means
        await Assertions.Expect(Page.Locator("select")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("invite-role")).ToContainTextAsync("Rolle: Manager");
        await Assertions.Expect(Page.GetByTestId("invite-role")).ToContainTextAsync("Das Team verwaltet nur der Besitzer.");
        await Page.GetByLabel("E-Mail-Adresse").FillAsync("keine-adresse");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Konto anlegen", Exact = true }).ClickAsync();
        await Assertions.Expect(Page.GetByText("Bitte geben Sie eine gültige E-Mail-Adresse ein.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Das Passwort muss mindestens 8 Zeichen lang sein.").Or(Page.GetByText("Bitte geben Sie ein vorläufiges Passwort ein."))).ToBeVisibleAsync();
    }
}
