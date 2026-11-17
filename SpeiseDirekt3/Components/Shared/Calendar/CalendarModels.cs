namespace SpeiseDirekt3.Components.Shared.Calendar;

/// <summary>FullCalendar view names used with <see cref="CalendarOptions.InitialView"/> and the toolbar.</summary>
public static class CalendarViews
{
    public const string Month = "dayGridMonth";
    public const string ListMonth = "listMonth";
    public const string Week = "dayGridWeek";
    public const string Year = "multiMonthYear";
}

/// <summary>
/// One all-day event of <see cref="FullCalendar"/>. Either a one-off event (<see cref="Start"/>, optional
/// <see cref="End"/>) or a weekly recurring one (<see cref="DaysOfWeek"/>, optional <see cref="StartRecur"/> /
/// <see cref="EndRecur"/>). Dates cross to the browser as <c>yyyy-MM-dd</c>, so there is no time-zone shift.
/// </summary>
public sealed record CalendarEvent
{
    /// <summary>Unique within the calendar; comes back in <see cref="CalendarEventClick"/> and <see cref="CalendarEventChange"/>.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>One-off event: its first day. Ignored when <see cref="DaysOfWeek"/> is set.</summary>
    public DateOnly? Start { get; init; }

    /// <summary>One-off event: the day <b>after</b> its last day (exclusive, as in FullCalendar). <c>null</c> = one day.</summary>
    public DateOnly? End { get; init; }

    /// <summary>Recurring event: the weekdays it repeats on.</summary>
    public IReadOnlyList<DayOfWeek>? DaysOfWeek { get; init; }

    /// <summary>Recurring event: first day of the recurrence (inclusive). <c>null</c> = since forever.</summary>
    public DateOnly? StartRecur { get; init; }

    /// <summary>Recurring event: the day <b>after</b> the last possible occurrence (exclusive). <c>null</c> = forever.</summary>
    public DateOnly? EndRecur { get; init; }

    public string? BackgroundColor { get; init; }
    public string? BorderColor { get; init; }
    public string? TextColor { get; init; }

    /// <summary>Extra CSS classes on the event element.</summary>
    public IReadOnlyList<string>? ClassNames { get; init; }

    /// <summary>Overrides <see cref="CalendarOptions.Editable"/> for this event (<c>null</c> = inherit).</summary>
    public bool? Editable { get; init; }

    /// <summary>Whether it can be dragged to another day (<c>null</c> = inherit).</summary>
    public bool? StartEditable { get; init; }

    /// <summary>Whether it can be stretched or shortened (<c>null</c> = inherit).</summary>
    public bool? DurationEditable { get; init; }

    /// <summary>Text for the <c>title</c> tooltip and the accessible name of the event element.</summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Per-occurrence extras, keyed by the occurrence's start date — for recurring events whose single days need a
    /// different look (e.g. overridden on that day) or tooltip.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, CalendarOccurrence>? Occurrences { get; init; }
}

/// <summary>Extra classes and tooltip for one occurrence of an event (see <see cref="CalendarEvent.Occurrences"/>).</summary>
public sealed record CalendarOccurrence(IReadOnlyList<string>? ClassNames = null, string? Tooltip = null);

/// <summary>A toolbar row: FullCalendar button names per chunk, e.g. <c>"prev,next today"</c>, <c>"title"</c>.</summary>
public sealed record CalendarToolbar(string Start, string Center, string End)
{
    public static readonly CalendarToolbar MonthAndList = new("prev,next today", "title", $"{CalendarViews.Month},{CalendarViews.ListMonth}");
}

/// <summary>Options of <see cref="FullCalendar"/>. Changes after the first render are applied in place (the visible month stays).</summary>
public sealed record CalendarOptions
{
    /// <summary>Only read on the first render.</summary>
    public string InitialView { get; init; } = CalendarViews.Month;

    /// <summary>Month shown first (default: <see cref="Today"/>). Only read on the first render.</summary>
    public DateOnly? InitialDate { get; init; }

    /// <summary>The day marked as today (default: the browser's date). Pass the server's date to follow its clock.</summary>
    public DateOnly? Today { get; init; }

    /// <summary>FullCalendar locale code; only the built-in <c>en</c> is vendored.</summary>
    public string Locale { get; init; } = "en";

    public DayOfWeek FirstDay { get; init; } = DayOfWeek.Monday;

    /// <summary>CSS height or <c>auto</c> (grows with its content, no inner scrolling).</summary>
    public string Height { get; init; } = "auto";

    public CalendarToolbar? HeaderToolbar { get; init; } = CalendarToolbar.MonthAndList;

    /// <summary>Labels of the toolbar buttons (keys: today, month, list, …).</summary>
    public IReadOnlyDictionary<string, string>? ButtonText { get; init; } = new Dictionary<string, string>
    {
        ["today"] = "Today", ["month"] = "Month", ["list"] = "List", ["week"] = "Week", ["year"] = "Year"
    };

    /// <summary>Days can be selected by dragging (raises <see cref="FullCalendar.OnSelect"/>).</summary>
    public bool Selectable { get; init; }

    /// <summary>Events can be dragged and resized (raises <see cref="FullCalendar.OnEventChange"/>).</summary>
    public bool Editable { get; init; }

    /// <summary>Rows of events per day before "+n more" (<c>null</c> = no limit).</summary>
    public int? DayMaxEventRows { get; init; } = 3;

    /// <summary>Text of the list view without events.</summary>
    public string NoEventsText { get; init; } = "Nothing this month";
}

/// <summary>A range of days: <see cref="End"/> is exclusive (the day after the last one).</summary>
public sealed record CalendarRange(DateOnly Start, DateOnly End)
{
    /// <summary>The last day of the range.</summary>
    public DateOnly Last => End.AddDays(-1);

    public int Days => End.DayNumber - Start.DayNumber;
}

/// <summary>An event (or one occurrence of a recurring event, <see cref="Date"/>) was clicked or activated with the keyboard.</summary>
public sealed record CalendarEventClick(string EventId, DateOnly Date);

public enum CalendarEventChangeKind { Moved, Resized }

/// <summary>
/// An event was dragged to another day (<see cref="CalendarEventChangeKind.Moved"/>) or stretched
/// (<see cref="CalendarEventChangeKind.Resized"/>). For a recurring event the dates are those of the dragged occurrence.
/// Ends are exclusive; <c>null</c> = one day.
/// </summary>
public sealed record CalendarEventChange(
    CalendarEventChangeKind Kind, string EventId, DateOnly OldStart, DateOnly? OldEnd, DateOnly Start, DateOnly? End)
{
    /// <summary>Days the start moved (negative = earlier).</summary>
    public int DeltaDays => Start.DayNumber - OldStart.DayNumber;
}

/// <summary>The visible dates changed (first render, prev/next, another view).</summary>
/// <param name="Start">First visible day (a month view also shows days of the neighbouring months).</param>
/// <param name="End">Day after the last visible day.</param>
/// <param name="CurrentStart">First day of the month (or the period) the view is about.</param>
/// <param name="CurrentEnd">Day after its last day.</param>
public sealed record CalendarDatesSet(DateOnly Start, DateOnly End, DateOnly CurrentStart, DateOnly CurrentEnd, string ViewType, string Title);
