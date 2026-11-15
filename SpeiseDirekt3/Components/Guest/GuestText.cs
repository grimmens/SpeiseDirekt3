using System.Globalization;
using System.Text.Json;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// Every text of the guest chrome (buttons, headings, notices, aria-labels, titles, the strings the guest scripts show)
/// and the price notation, in one place: <see cref="German"/> (formal "Sie", <c>€ 9,90</c>) for German menus and
/// <see cref="English"/> (<c>€9.90</c>) for every other menu language (<see cref="For"/>). Menu content (names,
/// descriptions, allergen names) is the owner's text and never translated.
/// Components get the instance as a cascading value from <see cref="GuestMenuView"/>
/// (<c>[CascadingParameter] public GuestText T</c>, default <see cref="German"/> — e.g. the notice pages).
/// The scripts read <see cref="ScriptJson"/> (the guest root's <c>data-guest-strings</c> attribute) through <c>ctx.text</c> / <c>ctx.t(key, values)</c> / <c>ctx.formatPrice(cents)</c> in <c>js/guest-menu.js</c>.
/// Texts with <c>{0}</c> are formatted by the methods below.
/// </summary>
public sealed class GuestText
{
    public static GuestText For(MenuLanguage language) => language == MenuLanguage.German ? German : English;

    /// <summary>German UI (also the default without a menu, e.g. "Menü nicht gefunden").</summary>
    public bool IsGerman { get; private init; }

    /// <summary>Code of the UI language (<c>de</c> / <c>en</c>), e.g. for <c>lang</c> on text in the UI language.</summary>
    public string Code => IsGerman ? "de" : "en";

    // --- Prices -------------------------------------------------------------------------------------------------

    /// <summary>Currency in front of the amount: <c>"€ "</c> (German) / <c>"€"</c> (English).</summary>
    public string PricePrefix { get; private init; } = "€ ";
    private NumberFormatInfo Numbers { get; init; } = new();

    /// <summary>Guest price: <c>€ 9,90</c> / <c>€9.90</c> (<c>€ 1.234,50</c> / <c>€1,234.50</c>).</summary>
    public string Price(decimal price) => PricePrefix + Amount(price);

    /// <summary>Just the amount: <c>9,90</c> / <c>9.90</c>.</summary>
    public string Amount(decimal price) => price.ToString("#,##0.00", Numbers);

    /// <summary>"ab € 3,40" / "from €3.40" when the item's variants differ in price, otherwise the price.</summary>
    public string ItemPrice(SpeiseDirekt.Model.MenuItem item) =>
        (GuestFormat.HasPriceRange(item) ? From + " " : "") + Price(item.Price);

    /// <summary>Price of an extra: <c>+ € 1,50</c> / <c>+ €1.50</c>.</summary>
    public string ExtraPrice(decimal price) => "+ " + Price(price);

    /// <summary>Text of a label chip (<see cref="ItemLabels"/>): German or English.</summary>
    public string Label(ItemLabel label) => label.GuestTextFor(IsGerman);

    /// <summary>Name of a menu language in the UI language ("Englisch" / "English").</summary>
    public string LanguageName(MenuLanguageInfo language) => IsGerman ? language.GermanName : language.EnglishName;

    // --- Page ---------------------------------------------------------------------------------------------------
    public string SkipToMenu { get; private init; } = "";
    public string MenuWord { get; private init; } = "";
    private string MenuOfFormat { get; init; } = "";
    public string MenuOf(string site) => string.Format(MenuOfFormat, site);
    public string NoDishes { get; private init; } = "";
    public string Close { get; private init; } = "";

    // --- Top bar, restaurant ---------------------------------------------------------------------------------------
    public string RestaurantInfo { get; private init; } = "";
    public string SearchMenu { get; private init; } = "";
    public string InfoTitle { get; private init; } = "";
    public string OpeningHours { get; private init; } = "";
    public string Contact { get; private init; } = "";
    public string MapsHint { get; private init; } = "";
    public string Call { get; private init; } = "";
    public string WriteEmail { get; private init; } = "";
    public string Website { get; private init; } = "";
    public string WriteReview { get; private init; } = "";

    // --- Languages ---------------------------------------------------------------------------------------------------
    /// <summary>Language button (aria-label) and title of the language sheet.</summary>
    public string ChooseLanguage { get; private init; } = "";
    /// <summary>Heading of the language list at the end of the page (reachable without JS).</summary>
    public string Languages { get; private init; } = "";

    // --- Categories -------------------------------------------------------------------------------------------------
    public string Categories { get; private init; } = "";
    public string ShowAllCategories { get; private init; } = "";

    // --- Items ---------------------------------------------------------------------------------------------------------
    public string Allergens { get; private init; } = "";
    public string LabelsList { get; private init; } = "";
    public string Prices { get; private init; } = "";
    public string From { get; private init; } = "";
    public string Share { get; private init; } = "";
    public string LinkToCopy { get; private init; } = "";
    public string Extras { get; private init; } = "";
    public string ComboOffers { get; private init; } = "";
    public string Separately { get; private init; } = "";
    private string YouSaveFormat { get; init; } = "";
    public string YouSave(string amount) => string.Format(YouSaveFormat, amount);

    // --- Favourites ----------------------------------------------------------------------------------------------------
    public string Favorites { get; private init; } = "";
    public string FavoriteDefault { get; private init; } = "";
    private string FavoriteAddFormat { get; init; } = "";
    private string FavoriteRemoveFormat { get; init; } = "";
    public string FavoriteAdd(string name) => string.Format(FavoriteAddFormat, name);
    public string FavoriteRemove(string name) => string.Format(FavoriteRemoveFormat, name);
    public string FavoritesEmpty { get; private init; } = "";
    private string VariantOfFormat { get; init; } = "";
    public string VariantOf(string name) => string.Format(VariantOfFormat, name);
    private string QuantityOfFormat { get; init; } = "";
    public string QuantityOf(string name) => string.Format(QuantityOfFormat, name);
    public string OnePortionLess { get; private init; } = "";
    public string OnePortionMore { get; private init; } = "";
    public string Total { get; private init; } = "";
    public string FavoritesNote { get; private init; } = "";
    public string ClearList { get; private init; } = "";
    public string ClearListConfirm { get; private init; } = "";
    public string Cancel { get; private init; } = "";
    public string YesClear { get; private init; } = "";

    // --- Filter, search -----------------------------------------------------------------------------------------------
    public string Filter { get; private init; } = "";
    public string FilterActive { get; private init; } = "";
    public string Reset { get; private init; } = "";
    public string ResetFilters { get; private init; } = "";
    public string FilterEmptyTitle { get; private init; } = "";
    public string FilterEmptyHint { get; private init; } = "";
    public string Diet { get; private init; } = "";
    public string Highlights { get; private init; } = "";
    public string WithoutAllergens { get; private init; } = "";
    public string WithoutAllergensHint { get; private init; } = "";
    public string ShowDishes { get; private init; } = "";
    public string Search { get; private init; } = "";
    public string SearchLabel { get; private init; } = "";
    public string SearchPlaceholder { get; private init; } = "";
    public string ClearSearch { get; private init; } = "";
    public string SearchEmptyHint { get; private init; } = "";

    // --- Scripts: placeholders {n}, {dishes}, {query}, {qty}, {price}; *One / *Other = singular / plural ----------------
    private IReadOnlyDictionary<string, string> Script { get; init; } = new Dictionary<string, string>();

    /// <summary>"1 Gericht" / "12 Gerichte" ("1 dish" / "12 dishes") — the same rule as the scripts.</summary>
    public string Dishes(int n) => (n == 1 ? Script["dishOne"] : Script["dishOther"]).Replace("{n}", n.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The strings and the price notation for <c>js/guest-*.js</c> as JSON (attribute <c>data-guest-strings</c> of the
    /// guest root).
    /// </summary>
    public string ScriptJson() => JsonSerializer.Serialize(new
    {
        lang = Code,
        price = new { prefix = PricePrefix, @decimal = Numbers.NumberDecimalSeparator, group = Numbers.NumberGroupSeparator },
        strings = Script
    });

    public static readonly GuestText German = new()
    {
        IsGerman = true,
        PricePrefix = "€ ",
        Numbers = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = new[] { 3 } },
        SkipToMenu = "Zur Speisekarte springen",
        MenuWord = "Speisekarte",
        MenuOfFormat = "Speisekarte von {0}",
        NoDishes = "Auf dieser Speisekarte stehen noch keine Gerichte.",
        Close = "Schließen",
        RestaurantInfo = "Informationen zum Restaurant",
        SearchMenu = "Speisekarte durchsuchen",
        InfoTitle = "Informationen",
        OpeningHours = "Öffnungszeiten",
        Contact = "Kontakt",
        MapsHint = "Route in Google Maps",
        Call = "Anrufen",
        WriteEmail = "E-Mail schreiben",
        Website = "Website",
        WriteReview = "Bewertung schreiben",
        ChooseLanguage = "Sprache wählen",
        Languages = "Sprachen",
        Categories = "Kategorien",
        ShowAllCategories = "Alle Kategorien anzeigen",
        Allergens = "Allergene",
        LabelsList = "Merkmale",
        Prices = "Preise",
        From = "ab",
        Share = "Teilen",
        LinkToCopy = "Link zum Kopieren",
        Extras = "Extras",
        ComboOffers = "Kombi-Angebote",
        Separately = "einzeln",
        YouSaveFormat = "Sie sparen {0}",
        Favorites = "Merkliste",
        FavoriteDefault = "Merken",
        FavoriteAddFormat = "{0} merken",
        FavoriteRemoveFormat = "{0} von der Merkliste entfernen",
        FavoritesEmpty = "Ihre Merkliste ist leer. Tippen Sie bei einem Gericht auf das Herz, um es sich zu merken.",
        VariantOfFormat = "Variante von {0}",
        QuantityOfFormat = "Anzahl {0}",
        OnePortionLess = "Eine Portion weniger",
        OnePortionMore = "Eine Portion mehr",
        Total = "Summe",
        FavoritesNote = "Die Merkliste wird nur auf diesem Gerät gespeichert. Bitte bestellen Sie beim Service.",
        ClearList = "Liste leeren",
        ClearListConfirm = "Merkliste wirklich leeren?",
        Cancel = "Abbrechen",
        YesClear = "Ja, leeren",
        Filter = "Filter",
        FilterActive = "Filter aktiv",
        Reset = "Zurücksetzen",
        ResetFilters = "Filter zurücksetzen",
        FilterEmptyTitle = "Keine Gerichte passen zu Ihren Filtern.",
        FilterEmptyHint = "Entfernen Sie einzelne Filter oder setzen Sie alle zurück.",
        Diet = "Ernährung",
        Highlights = "Hinweise",
        WithoutAllergens = "Ohne Allergene",
        WithoutAllergensHint = "Gerichte mit den gewählten Allergenen werden ausgeblendet.",
        ShowDishes = "Gerichte anzeigen",
        Search = "Suche",
        SearchLabel = "Speisen und Getränke suchen",
        SearchPlaceholder = "Gericht, Zutat oder Merkmal",
        ClearSearch = "Eingabe löschen",
        SearchEmptyHint = "Versuchen Sie einen anderen Begriff, zum Beispiel eine Zutat oder „vegan“.",
        Script = new Dictionary<string, string>
        {
            ["dishOne"] = "1 Gericht",
            ["dishOther"] = "{n} Gerichte",
            ["filterApply"] = "{dishes} anzeigen",
            ["filterButton"] = "Filter",
            ["filterButtonActive"] = "Filter ({n} aktiv)",
            ["searchNoResults"] = "Keine Treffer für „{query}“",
            ["searchResultOne"] = "1 Ergebnis",
            ["searchResultOther"] = "{n} Ergebnisse",
            ["searchFilterOnly"] = "Filter aktiv: nur passende Gerichte",
            ["searchFilterHiddenOne"] = "Filter aktiv: 1 weiterer Treffer ausgeblendet",
            ["searchFilterHiddenOther"] = "Filter aktiv: {n} weitere Treffer ausgeblendet",
            ["favoriteUnit"] = "{qty} × {price}",
            ["shareCopied"] = "Link kopiert"
        }
    };

    public static readonly GuestText English = new()
    {
        IsGerman = false,
        PricePrefix = "€",
        Numbers = new NumberFormatInfo { NumberDecimalSeparator = ".", NumberGroupSeparator = ",", NumberGroupSizes = new[] { 3 } },
        SkipToMenu = "Skip to the menu",
        MenuWord = "Menu",
        MenuOfFormat = "Menu of {0}",
        NoDishes = "There are no dishes on this menu yet.",
        Close = "Close",
        RestaurantInfo = "About the restaurant",
        SearchMenu = "Search the menu",
        InfoTitle = "Information",
        OpeningHours = "Opening hours",
        Contact = "Contact",
        MapsHint = "Directions in Google Maps",
        Call = "Call",
        WriteEmail = "Send an e-mail",
        Website = "Website",
        WriteReview = "Write a review",
        ChooseLanguage = "Choose language",
        Languages = "Languages",
        Categories = "Categories",
        ShowAllCategories = "Show all categories",
        Allergens = "Allergens",
        LabelsList = "Labels",
        Prices = "Prices",
        From = "from",
        Share = "Share",
        LinkToCopy = "Link to copy",
        Extras = "Extras",
        ComboOffers = "Combo offers",
        Separately = "separately",
        YouSaveFormat = "You save {0}",
        Favorites = "Favourites",
        FavoriteDefault = "Add to favourites",
        FavoriteAddFormat = "Add {0} to favourites",
        FavoriteRemoveFormat = "Remove {0} from favourites",
        FavoritesEmpty = "Your favourites list is empty. Tap the heart on a dish to add it.",
        VariantOfFormat = "Variant of {0}",
        QuantityOfFormat = "Quantity of {0}",
        OnePortionLess = "One portion less",
        OnePortionMore = "One portion more",
        Total = "Total",
        FavoritesNote = "Your favourites are only saved on this device. Please order from our staff.",
        ClearList = "Clear list",
        ClearListConfirm = "Clear your favourites list?",
        Cancel = "Cancel",
        YesClear = "Yes, clear",
        Filter = "Filter",
        FilterActive = "Filter on",
        Reset = "Reset",
        ResetFilters = "Reset filters",
        FilterEmptyTitle = "No dishes match your filters.",
        FilterEmptyHint = "Remove some of the filters or reset them all.",
        Diet = "Diet",
        Highlights = "Highlights",
        WithoutAllergens = "Without allergens",
        WithoutAllergensHint = "Dishes containing the selected allergens are hidden.",
        ShowDishes = "Show dishes",
        Search = "Search",
        SearchLabel = "Search food and drinks",
        SearchPlaceholder = "Dish, ingredient or label",
        ClearSearch = "Clear search",
        SearchEmptyHint = "Try another word, for example an ingredient or “vegan”.",
        Script = new Dictionary<string, string>
        {
            ["dishOne"] = "1 dish",
            ["dishOther"] = "{n} dishes",
            ["filterApply"] = "Show {dishes}",
            ["filterButton"] = "Filter",
            ["filterButtonActive"] = "Filter ({n} active)",
            ["searchNoResults"] = "No results for “{query}”",
            ["searchResultOne"] = "1 result",
            ["searchResultOther"] = "{n} results",
            ["searchFilterOnly"] = "Filter on: matching dishes only",
            ["searchFilterHiddenOne"] = "Filter on: 1 more result hidden",
            ["searchFilterHiddenOther"] = "Filter on: {n} more results hidden",
            ["favoriteUnit"] = "{qty} × {price}",
            ["shareCopied"] = "Link copied"
        }
    };
}
