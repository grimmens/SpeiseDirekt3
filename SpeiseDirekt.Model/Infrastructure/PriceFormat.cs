using System.Globalization;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// Prices in the back office, formatted like the guest menu ("€ 13.90") and independent of the server's
/// culture (<c>ToString("C")</c> showed "$13.90" on an en-US host).
/// </summary>
public static class PriceFormat
{
    public static string Euro(decimal price) => "€ " + price.ToString("F2", CultureInfo.InvariantCulture);
}
