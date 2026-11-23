using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SpeiseDirekt.Infrastructure;

namespace SpeiseDirekt.Model
{
    public class QRCode : IAppUserEntity, IValidatableObject
    {
        public Guid Id { get; set; }

        [Required(ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.QrCode_TitleRequired)), StringLength(100, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.QrCode_TitleTooLong))]
        public string Title { get; set; } = string.Empty;

        [ForeignKey(nameof(Menu))]
        public Guid? MenuId { get; set; }
        public Menu? Menu { get; set; }
        public bool IsTimeTableBased { get; set; }
        public bool IsCalendarBased { get; set; }
        [NotMapped]
        public bool IsNotTimeTableBased
        {
            get
            {
                return !IsTimeTableBased;
            }
            set
            {
                IsTimeTableBased = !value;
            }
        }
        [NotMapped]
        public bool IsNotCalendarBased
        {
            get
            {
                return !IsCalendarBased;
            }
            set
            {
                IsCalendarBased = !value;
            }
        }
        public List<TimeTableEntry>? TimeTableEntries { get; set; } = new List<TimeTableEntry>();
        public List<CalendarEntry>? CalendarEntries { get; set; } = new List<CalendarEntry>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid ApplicationUserId { get; set; }

        // Messages in the current (UI) culture (AP12): the back office's language; the API's is the server's (English).
        private static string Text(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
        private static string Time(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        private static string Date(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Validate TimeTable entries
            if (IsTimeTableBased)
            {
                if (TimeTableEntries == null || !TimeTableEntries.Any())
                {
                    yield return new ValidationResult(
                            ModelText.QrCode_TimeTableRequired,
                            new[] { nameof(TimeTableEntries) }
                        );
                }
                else
                {
                    // 1) Every entry must have a MenuId
                    foreach (var entry in TimeTableEntries)
                    {
                        if (entry.MenuId == Guid.Empty)
                        {
                            yield return new ValidationResult(
                                ModelText.QrCode_TimeTableMenuRequired,
                                new[] { nameof(TimeTableEntries) }
                            );
                            yield break;
                        }
                    }

                    // 2) No overlapping timeframes
                    var sorted = TimeTableEntries
                        .OrderBy(e => e.StartTime)
                        .ToList();

                    for (int i = 1; i < sorted.Count; i++)
                    {
                        var prev = sorted[i - 1];
                        var curr = sorted[i];

                        if (prev.EndTime > curr.StartTime)
                        {
                            yield return new ValidationResult(
                                Text(ModelText.QrCode_TimeTableOverlap, Time(prev.StartTime), Time(prev.EndTime), Time(curr.StartTime), Time(curr.EndTime)),
                                new[] { nameof(TimeTableEntries) }
                            );
                            yield break;
                        }
                    }
                }
            }

            // Validate Calendar entries
            if (IsCalendarBased)
            {
                if (CalendarEntries == null || !CalendarEntries.Any())
                {
                    yield return new ValidationResult(
                            ModelText.QrCode_CalendarRequired,
                            new[] { nameof(CalendarEntries) }
                        );
                }
                else
                {
                    // Every calendar entry must have a MenuId
                    foreach (var entry in CalendarEntries)
                    {
                        if (entry.MenuId == Guid.Empty)
                        {
                            yield return new ValidationResult(
                                ModelText.QrCode_CalendarMenuRequired,
                                new[] { nameof(CalendarEntries) }
                            );
                            yield break;
                        }

                        // If EndDate is specified, it must be >= Date
                        if (entry.EndDate.HasValue && entry.EndDate < entry.Date)
                        {
                            yield return new ValidationResult(
                                ModelText.QrCode_CalendarEndBeforeStart,
                                new[] { nameof(CalendarEntries) }
                            );
                            yield break;
                        }
                    }

                    // Check for overlapping date ranges. A single date (or date range) beats a weekly entry on
                    // the same day (MenuService), so only entries of the same kind collide: two date ranges, or
                    // two weekly entries on the same weekday whose periods overlap (CalendarSchedule.Collide; the
                    // back-office calendar marks the same pairs live). Sorted by date, comparing neighbours finds
                    // the first collision.
                    var sortedCalendar = CalendarEntries
                        .Where(e => e.RecurringDayOfWeek == null)
                        .OrderBy(e => e.Date)
                        .ToList();

                    for (int i = 1; i < sortedCalendar.Count; i++)
                    {
                        var prev = sortedCalendar[i - 1];
                        var curr = sortedCalendar[i];

                        var prevEnd = prev.EndDate ?? prev.Date;

                        if (CalendarSchedule.Collide(prev, curr))
                        {
                            yield return new ValidationResult(
                                Text(ModelText.QrCode_CalendarOverlap, Date(prev.Date), Date(prevEnd), Date(curr.Date), Date(curr.EndDate ?? curr.Date)),
                                new[] { nameof(CalendarEntries) }
                            );
                            yield break;
                        }
                    }

                    foreach (var weekday in CalendarEntries.Where(e => e.RecurringDayOfWeek != null).GroupBy(e => e.RecurringDayOfWeek!.Value))
                    {
                        var sortedWeekly = weekday.OrderBy(e => e.Date).ToList();
                        for (int i = 1; i < sortedWeekly.Count; i++)
                        {
                            var prev = sortedWeekly[i - 1];
                            var curr = sortedWeekly[i];

                            // Without an end date a weekly entry repeats forever
                            if (CalendarSchedule.Collide(prev, curr))
                            {
                                var dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(weekday.Key);
                                yield return new ValidationResult(
                                    prev.EndDate is { } end
                                        ? Text(ModelText.QrCode_WeeklyOverlap, dayName, Date(prev.Date), Date(end), Date(curr.Date))
                                        : Text(ModelText.QrCode_WeeklyOverlapOpen, dayName, Date(prev.Date), Date(curr.Date)),
                                    new[] { nameof(CalendarEntries) }
                                );
                                yield break;
                            }
                        }
                    }
                }
            }

            // At least one type must be active if no direct menu is set
            if (!MenuId.HasValue && !IsTimeTableBased && !IsCalendarBased)
            {
                yield return new ValidationResult(
                    ModelText.QrCode_MenuRequired,
                    new[] { nameof(MenuId) }
                );
            }
        }
    }
}
