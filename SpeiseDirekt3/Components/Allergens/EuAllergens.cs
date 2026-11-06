using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Allergens;

/// <summary>
/// The 14 allergens that EU Regulation 1169/2011 requires restaurants to declare, with the letter codes
/// used on Austrian menu cards (A–R, without I, J, K and Q). Used by the quick-fill on the allergens page.
/// </summary>
public static class EuAllergens
{
    public sealed record Entry(string Code, string German, string English)
    {
        /// <summary>German names for German menus, English for every other menu language.</summary>
        public string NameFor(MenuLanguage language) => language == MenuLanguage.German ? German : English;
    }

    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry("A", "Glutenhaltiges Getreide", "Cereals containing gluten"),
        new Entry("B", "Krebstiere", "Crustaceans"),
        new Entry("C", "Eier", "Eggs"),
        new Entry("D", "Fisch", "Fish"),
        new Entry("E", "Erdnüsse", "Peanuts"),
        new Entry("F", "Soja", "Soybeans"),
        new Entry("G", "Milch / Laktose", "Milk"),
        new Entry("H", "Schalenfrüchte", "Nuts"),
        new Entry("L", "Sellerie", "Celery"),
        new Entry("M", "Senf", "Mustard"),
        new Entry("N", "Sesam", "Sesame"),
        new Entry("O", "Sulfite", "Sulphites"),
        new Entry("P", "Lupinen", "Lupin"),
        new Entry("R", "Weichtiere", "Molluscs"),
    };

    /// <summary>Codes are compared case-insensitively and without surrounding blanks ("a " = "A").</summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>The entries whose code does not exist yet among <paramref name="existingCodes"/>.</summary>
    public static IReadOnlyList<Entry> Missing(IEnumerable<string> existingCodes)
    {
        var existing = existingCodes.Select(NormalizeCode).ToHashSet();
        return All.Where(e => !existing.Contains(e.Code)).ToList();
    }
}
