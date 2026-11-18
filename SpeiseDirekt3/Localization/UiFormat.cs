using System.Globalization;

namespace SpeiseDirekt3.Localization;

/// <summary>
/// User-visible numbers, prices and dates of the back office in the current culture (<see cref="AppCultures"/>).
/// The currency is always the euro, never the server culture's (<c>ToString("C")</c> once showed "$13.90").
/// <para>Machine values — HTML attributes, CSS (<c>style="width: 12.5%"</c>), <c>data-*</c>, JSON, URLs, the editor
/// preview's postMessage, change fingerprints, chart and calendar data — never go through here: they are written with
/// <see cref="CultureInfo.InvariantCulture"/> (a German culture would turn "12.5" into "12,5" and silently break them).</para>
/// </summary>
public static class UiFormat
{
    private const string NoBreakSpace = " ";

    /// <summary>A price: <c>5,90 €</c> (German, no-break space) / <c>€5.90</c> (English); <c>1.234,50 €</c> / <c>€1,234.50</c>.</summary>
    public static string Price(decimal price)
    {
        var culture = CultureInfo.CurrentCulture;
        var amount = price.ToString("#,##0.00", culture);
        return AppCultures.IsGerman ? amount + NoBreakSpace + "€" : "€" + amount;
    }

    /// <summary>A whole number with the culture's group separator: <c>1.234</c> / <c>1,234</c>.</summary>
    public static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>A short date: <c>18.11.2026</c> / <c>11/18/2026</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>A date with the month's name: <c>18. November 2026</c> / <c>November 18, 2026</c>.</summary>
    public static string LongDate(DateOnly date) =>
        date.ToString(AppCultures.IsGerman ? "d. MMMM yyyy" : "MMMM d, yyyy", CultureInfo.CurrentCulture);

    /// <summary>A time of day: <c>14:30</c> / <c>2:30 PM</c>.</summary>
    public static string Time(TimeOnly time) => time.ToString("t", CultureInfo.CurrentCulture);
}
