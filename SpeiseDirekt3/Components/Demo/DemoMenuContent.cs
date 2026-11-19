using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Demo;

/// <summary>A dish of the demo menu. <see cref="Photo"/> is the file stem of its picture in <c>wwwroot/images/demo</c> (null = no photo).</summary>
public sealed record DemoDish(
    string Category, string Name, string Description, decimal Price, string[] Allergens, string? Photo = null,
    bool Vegan = false, bool Vegetarian = false, bool Spicy = false, bool Halal = false, bool AlcoholFree = false,
    bool New = false, bool Popular = false,
    (string Name, decimal Price)[]? Variants = null, (string Name, decimal Price)[]? Extras = null);

/// <summary>A category of the demo menu with its optional subtitle (<c>Category.Description</c>).</summary>
public sealed record DemoCategory(string Name, string? Subtitle = null);

/// <summary>The combo offer: triggered by <see cref="Trigger"/>, includes <see cref="Includes"/> (dish names).</summary>
public sealed record DemoCombo(string Name, string Description, decimal Price, string Trigger, string[] Includes);

/// <summary>The made-up restaurant's profile in one language (no phone number and no social links: a live page must not
/// send visitors to a real number or a stranger's account).</summary>
public sealed record DemoProfile(string Name, string Description, string Address, string OpeningHours, string Email, string Website);

/// <summary>One language version of the demo menu, served at <see cref="Path"/>.</summary>
public sealed record DemoMenuVersion(
    MenuLanguage Language, string Path, string MenuName, string MenuDescription, IReadOnlyList<DemoCategory> Categories,
    IReadOnlyList<DemoDish> Dishes, IReadOnlyList<(string Code, string Name)> Allergens, DemoCombo Combo, DemoProfile Profile);

/// <summary>
/// The content of the live demo (<c>/demo</c>, <see cref="DemoMenuPage"/>): the made-up restaurant "Trattoria Lago" the
/// landing page's screenshots show — German (the reference) and a hand-translated English version, linked as language
/// versions. Code only: no database rows, no tenant. The E2E project seeds the same German content (<c>GuestDemoData</c>)
/// for the landing page's screenshots and the layout tests, so the screenshots and the live demo show the same menu; its
/// tour tool also renders the photos (<c>LandingScreenshotTour.Capture_demo_images</c> → <c>wwwroot/images/demo</c>).
/// Product truth: only features the app has.
/// </summary>
public static class DemoMenuContent
{
    /// <summary>Folder of the photos below <c>wwwroot</c>: one <c>{Photo}.webp</c> per dish photo, plus <see cref="CoverPhoto"/> and <see cref="LogoPhoto"/>.</summary>
    public const string ImageFolder = "images/demo";

    public const string CoverPhoto = "cover";
    public const string LogoPhoto = "logo";

    /// <summary>Path of a photo below <c>wwwroot</c> (<c>images/demo/{stem}.webp</c>).</summary>
    public static string ImagePath(string stem) => $"{ImageFolder}/{stem}.webp";

    public const string RestaurantName = "Trattoria Lago";

    public static readonly DemoMenuVersion German = new(
        MenuLanguage.German,
        "/demo",
        "Trattoria Lago",
        "Italienische Küche am See · seit 1987",
        [
            new("Antipasti", "Zum Teilen für den ganzen Tisch"),
            new("Pasta & Risotto", "Hausgemachte Pasta, täglich frisch"),
            new("Pizza", "Aus dem Holzofen · auf Wunsch auch glutenfrei"),
            new("Dolci"),
            new("Alkoholfreie Getränke"),
            new("Weine", "Offene Weine aus Österreich und Italien"),
            new("Kaffee")
        ],
        [
            new("Antipasti", "Bruschetta Classica", "Geröstetes Landbrot mit Tomaten, Basilikum und Knoblauch", 7.90m, ["A"], "bruschetta-classica", Vegan: true),
            new("Antipasti", "Vitello Tonnato", "Zartes Kalbfleisch mit Thunfischcreme und Kapern", 13.50m, ["C", "D"], "vitello-tonnato"),
            new("Antipasti", "Burrata", "Cremige Burrata auf Kirschtomaten mit Basilikumöl", 12.90m, ["G"], "burrata", Vegetarian: true),
            new("Antipasti", "Carpaccio vom Rind", "Hauchdünnes Rinderfilet, Rucola, Parmesan, Zitrone", 14.50m, ["G"], "carpaccio", New: true),
            new("Pasta & Risotto", "Spaghetti Carbonara", "Guanciale, Eigelb, Pecorino, schwarzer Pfeffer", 14.90m, ["A", "C", "G"], "spaghetti-carbonara", Popular: true),
            new("Pasta & Risotto", "Penne all'Arrabbiata", "Scharfe Tomatensauce mit Chili und Petersilie", 12.50m, ["A"], "penne-arrabbiata", Vegan: true, Spicy: true, Popular: true, Halal: true),
            new("Pasta & Risotto", "Risotto ai Funghi", "Cremiges Risotto mit Steinpilzen und Parmesan", 16.90m, ["G", "L"], Vegetarian: true),
            new("Pasta & Risotto", "Tagliatelle al Ragù", "Hausgemachte Bandnudeln mit Rinderragout", 15.40m, ["A", "C", "L"], "tagliatelle-ragu"),
            new("Pizza", "Margherita", "Tomate, Fior di Latte, Basilikum", 10.50m, ["A", "G"], "margherita", Vegetarian: true, Popular: true,
                Extras: [("+ Büffelmozzarella", 3.50m), ("+ Rucola", 1.50m), ("+ Prosciutto crudo", 3.90m)]),
            new("Pizza", "Diavola", "Tomate, Mozzarella, scharfe Salami, Chili", 13.90m, ["A", "G"], "diavola", Spicy: true),
            new("Pizza", "Quattro Formaggi", "Mozzarella, Gorgonzola, Parmesan, Taleggio", 14.50m, ["A", "G"], "quattro-formaggi", Vegetarian: true),
            new("Dolci", "Tiramisu", "Mascarpone, Espresso, Savoiardi, Kakao", 7.50m, ["A", "C", "G"], "tiramisu", Vegetarian: true, Popular: true),
            new("Dolci", "Panna Cotta", "Mit Beerenragout und frischer Minze", 6.90m, ["G"], "panna-cotta", Vegetarian: true),
            new("Alkoholfreie Getränke", "Mineralwasser 0,5 l", "Prickelnd oder still", 4.20m, [], Vegan: true, AlcoholFree: true),
            new("Alkoholfreie Getränke", "Hausgemachte Limonade", "Zitrone-Minze oder Holunder", 4.20m, [], Vegan: true, New: true, AlcoholFree: true,
                Variants: [("0,3 l", 4.20m), ("0,5 l", 5.90m)]),
            new("Alkoholfreie Getränke", "Apfelsaft gespritzt 0,5 l", "", 4.50m, [], Vegan: true, AlcoholFree: true),
            new("Weine", "Grüner Veltliner", "Weingut am Hang, Wachau", 4.80m, ["O"], Popular: true,
                Variants: [("1/8 l", 4.80m), ("1/4 l", 9.20m), ("Flasche 0,75 l", 26.00m)]),
            new("Weine", "Chianti Classico 1/8 l", "Toskana, DOCG", 6.20m, ["O"]),
            new("Weine", "Prosecco 0,1 l", "Extra Dry", 5.90m, ["O"]),
            new("Kaffee", "Espresso", "", 2.90m, [], Vegan: true),
            new("Kaffee", "Cappuccino", "Mit aufgeschäumter Milch", 4.20m, ["G"], Vegetarian: true)
        ],
        [("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("D", "Fisch"), ("G", "Milch / Laktose"), ("L", "Sellerie"), ("O", "Sulfite")],
        new DemoCombo("Pranzo", "Pizza Margherita, Tiramisu und Mineralwasser", 19.90m, "Margherita", ["Tiramisu", "Mineralwasser 0,5 l"]),
        new DemoProfile(
            RestaurantName,
            "Familienbetrieb seit 1987: hausgemachte Pasta, Pizza aus dem Holzofen und Weine aus Österreich und Italien – mit Blick auf den See.",
            "Seepromenade 12\n5310 Mondsee",
            "Mo–Fr 11:30–14:30, 17:30–22:00\nSa, So 11:30–22:00\nDienstag Ruhetag",
            "ciao@trattoria-lago.example",
            "https://trattoria-lago.example"));

    /// <summary>The English version (translated by hand, as owners do: the app has no machine translation).</summary>
    public static readonly DemoMenuVersion English = new(
        MenuLanguage.English,
        "/demo/en",
        "Trattoria Lago",
        "Italian cooking by the lake · since 1987",
        [
            new("Antipasti", "To share with the whole table"),
            new("Pasta & Risotto", "Fresh homemade pasta every day"),
            new("Pizza", "From the wood-fired oven · gluten-free on request"),
            new("Dolci"),
            new("Soft drinks"),
            new("Wines", "Wines by the glass from Austria and Italy"),
            new("Coffee")
        ],
        [
            new("Antipasti", "Bruschetta Classica", "Toasted country bread with tomatoes, basil and garlic", 7.90m, ["A"], "bruschetta-classica", Vegan: true),
            new("Antipasti", "Vitello Tonnato", "Tender veal with tuna cream and capers", 13.50m, ["C", "D"], "vitello-tonnato"),
            new("Antipasti", "Burrata", "Creamy burrata on cherry tomatoes with basil oil", 12.90m, ["G"], "burrata", Vegetarian: true),
            new("Antipasti", "Beef carpaccio", "Wafer-thin beef fillet, rocket, parmesan, lemon", 14.50m, ["G"], "carpaccio", New: true),
            new("Pasta & Risotto", "Spaghetti Carbonara", "Guanciale, egg yolk, pecorino, black pepper", 14.90m, ["A", "C", "G"], "spaghetti-carbonara", Popular: true),
            new("Pasta & Risotto", "Penne all'Arrabbiata", "Spicy tomato sauce with chilli and parsley", 12.50m, ["A"], "penne-arrabbiata", Vegan: true, Spicy: true, Popular: true, Halal: true),
            new("Pasta & Risotto", "Risotto ai Funghi", "Creamy risotto with porcini and parmesan", 16.90m, ["G", "L"], Vegetarian: true),
            new("Pasta & Risotto", "Tagliatelle al Ragù", "Homemade ribbon pasta with beef ragù", 15.40m, ["A", "C", "L"], "tagliatelle-ragu"),
            new("Pizza", "Margherita", "Tomato, fior di latte, basil", 10.50m, ["A", "G"], "margherita", Vegetarian: true, Popular: true,
                Extras: [("+ Buffalo mozzarella", 3.50m), ("+ Rocket", 1.50m), ("+ Prosciutto crudo", 3.90m)]),
            new("Pizza", "Diavola", "Tomato, mozzarella, spicy salami, chilli", 13.90m, ["A", "G"], "diavola", Spicy: true),
            new("Pizza", "Quattro Formaggi", "Mozzarella, gorgonzola, parmesan, taleggio", 14.50m, ["A", "G"], "quattro-formaggi", Vegetarian: true),
            new("Dolci", "Tiramisu", "Mascarpone, espresso, savoiardi, cocoa", 7.50m, ["A", "C", "G"], "tiramisu", Vegetarian: true, Popular: true),
            new("Dolci", "Panna Cotta", "With berry compote and fresh mint", 6.90m, ["G"], "panna-cotta", Vegetarian: true),
            new("Soft drinks", "Mineral water 0.5 l", "Sparkling or still", 4.20m, [], Vegan: true, AlcoholFree: true),
            new("Soft drinks", "Homemade lemonade", "Lemon and mint or elderflower", 4.20m, [], Vegan: true, New: true, AlcoholFree: true,
                Variants: [("0.3 l", 4.20m), ("0.5 l", 5.90m)]),
            new("Soft drinks", "Apple juice spritzer 0.5 l", "", 4.50m, [], Vegan: true, AlcoholFree: true),
            new("Wines", "Grüner Veltliner", "Hillside winery, Wachau", 4.80m, ["O"], Popular: true,
                Variants: [("1/8 l", 4.80m), ("1/4 l", 9.20m), ("Bottle 0.75 l", 26.00m)]),
            new("Wines", "Chianti Classico 1/8 l", "Tuscany, DOCG", 6.20m, ["O"]),
            new("Wines", "Prosecco 0.1 l", "Extra dry", 5.90m, ["O"]),
            new("Coffee", "Espresso", "", 2.90m, [], Vegan: true),
            new("Coffee", "Cappuccino", "With frothed milk", 4.20m, ["G"], Vegetarian: true)
        ],
        [("A", "Cereals containing gluten"), ("C", "Eggs"), ("D", "Fish"), ("G", "Milk / lactose"), ("L", "Celery"), ("O", "Sulphites")],
        new DemoCombo("Pranzo", "Pizza Margherita, tiramisu and mineral water", 19.90m, "Margherita", ["Tiramisu", "Mineral water 0.5 l"]),
        new DemoProfile(
            RestaurantName,
            "A family business since 1987: homemade pasta, pizza from the wood-fired oven and wines from Austria and Italy – with a view of the lake.",
            "Seepromenade 12\n5310 Mondsee",
            "Mon–Fri 11:30–14:30, 17:30–22:00\nSat, Sun 11:30–22:00\nClosed on Tuesdays",
            "ciao@trattoria-lago.example",
            "https://trattoria-lago.example"));

    /// <summary>The language versions in the order of the language switch.</summary>
    public static readonly IReadOnlyList<DemoMenuVersion> Versions = [German, English];

    /// <summary>Every photo stem (dishes, cover, logo) — the files the page needs in <see cref="ImageFolder"/>.</summary>
    public static IEnumerable<string> Photos =>
        German.Dishes.Select(d => d.Photo).OfType<string>().Concat(English.Dishes.Select(d => d.Photo).OfType<string>())
            .Append(CoverPhoto).Append(LogoPhoto).Distinct();
}
