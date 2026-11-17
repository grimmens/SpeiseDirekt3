using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// The rules of a calendar-based QR code in one place: which <see cref="CalendarEntry"/> applies on a day
/// (<c>MenuService</c> for guests, the back-office calendar editor for the preview) and which entries
/// collide (<see cref="QRCode.Validate"/>, the editor's live conflict marks).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Without <see cref="CalendarEntry.RecurringDayOfWeek"/>: every day from
/// <see cref="CalendarEntry.Date"/> to <c>EndDate ?? Date</c> (a single day or a period).</description></item>
/// <item><description>With <see cref="CalendarEntry.RecurringDayOfWeek"/>: every such weekday from
/// <see cref="CalendarEntry.Date"/>, until <see cref="CalendarEntry.EndDate"/> or forever.</description></item>
/// <item><description>Several entries on one day: a single date or period beats a weekly entry, then the earliest
/// <see cref="CalendarEntry.Date"/> wins (input order on a tie). No entry: the QR code's fallback applies.</description></item>
/// <item><description>Only entries of the same kind collide: two periods that share a day, or two weekly entries on the
/// same weekday whose periods overlap. A period inside a weekly entry's period is allowed (it wins).</description></item>
/// </list>
/// </remarks>
public static class CalendarSchedule
{
    /// <summary>Whether <paramref name="entry"/> covers <paramref name="date"/> (precedence aside).</summary>
    public static bool IsActiveOn(CalendarEntry entry, DateOnly date)
    {
        if (entry.RecurringDayOfWeek is null)
            return entry.Date <= date && date <= (entry.EndDate ?? entry.Date);

        return date.DayOfWeek == entry.RecurringDayOfWeek
            && date >= entry.Date
            && (entry.EndDate is null || date <= entry.EndDate);
    }

    /// <summary>
    /// The entry whose menu guests get on <paramref name="date"/>, or <c>null</c> when none covers it.
    /// One-off entries (single date or period) before weekly ones, then the earliest <see cref="CalendarEntry.Date"/>.
    /// </summary>
    public static CalendarEntry? ActiveEntry(IEnumerable<CalendarEntry>? entries, DateOnly date) =>
        (entries ?? Enumerable.Empty<CalendarEntry>())
            .Where(e => IsActiveOn(e, date))
            .OrderBy(e => e.RecurringDayOfWeek.HasValue) // one-off entries before weekly ones
            .ThenBy(e => e.Date)
            .FirstOrDefault();

    /// <summary>
    /// Whether two entries would be refused together: both one-off and their days overlap, or both weekly on the same
    /// weekday and their periods overlap (a weekly entry without an end repeats forever). One-off and weekly never collide.
    /// Entries whose end lies before their start cover no day and collide with nothing.
    /// </summary>
    public static bool Collide(CalendarEntry a, CalendarEntry b)
    {
        if (ReferenceEquals(a, b) || HasEndBeforeStart(a) || HasEndBeforeStart(b))
            return false;

        if (a.RecurringDayOfWeek is null && b.RecurringDayOfWeek is null)
            return a.Date <= (b.EndDate ?? b.Date) && b.Date <= (a.EndDate ?? a.Date);

        if (a.RecurringDayOfWeek is { } dayA && b.RecurringDayOfWeek is { } dayB && dayA == dayB)
            return a.Date <= (b.EndDate ?? DateOnly.MaxValue) && b.Date <= (a.EndDate ?? DateOnly.MaxValue);

        return false;
    }

    /// <summary>Every pair of entries that <see cref="Collide"/>, in input order.</summary>
    public static IReadOnlyList<(CalendarEntry First, CalendarEntry Second)> Conflicts(IReadOnlyList<CalendarEntry>? entries)
    {
        var result = new List<(CalendarEntry, CalendarEntry)>();
        if (entries is null)
            return result;
        for (var i = 0; i < entries.Count; i++)
            for (var j = i + 1; j < entries.Count; j++)
                if (Collide(entries[i], entries[j]))
                    result.Add((entries[i], entries[j]));
        return result;
    }

    /// <summary><see cref="CalendarEntry.EndDate"/> lies before <see cref="CalendarEntry.Date"/> (refused by validation).</summary>
    public static bool HasEndBeforeStart(CalendarEntry entry) => entry.EndDate is { } end && end < entry.Date;
}
