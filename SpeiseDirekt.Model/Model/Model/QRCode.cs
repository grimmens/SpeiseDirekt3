using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class QRCode : IAppUserEntity, IValidatableObject
    {
        public Guid Id { get; set; }

        [Required, StringLength(100)]
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

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Validate TimeTable entries
            if (IsTimeTableBased)
            {
                if (TimeTableEntries == null || !TimeTableEntries.Any())
                {
                    yield return new ValidationResult(
                            $"Es muss zumindest einen Zeitplan-Eintrag geben.",
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
                                $"Ein Zeitplan-Eintrag muss ein Menü auswählen.",
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
                                $"Die Zeitfenster überschneiden sich: {prev.StartTime:HH:mm}–{prev.EndTime:HH:mm} und {curr.StartTime:HH:mm}–{curr.EndTime:HH:mm}.",
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
                            $"Es muss zumindest einen Kalender-Eintrag geben.",
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
                                $"Ein Kalender-Eintrag muss ein Menü auswählen.",
                                new[] { nameof(CalendarEntries) }
                            );
                            yield break;
                        }

                        // If EndDate is specified, it must be >= Date
                        if (entry.EndDate.HasValue && entry.EndDate < entry.Date)
                        {
                            yield return new ValidationResult(
                                $"Das End-Datum muss nach oder gleich dem Start-Datum sein.",
                                new[] { nameof(CalendarEntries) }
                            );
                            yield break;
                        }
                    }

                    // Check for overlapping date ranges
                    var sortedCalendar = CalendarEntries
                        .OrderBy(e => e.Date)
                        .ToList();

                    for (int i = 1; i < sortedCalendar.Count; i++)
                    {
                        var prev = sortedCalendar[i - 1];
                        var curr = sortedCalendar[i];

                        var prevEnd = prev.EndDate ?? prev.Date;

                        if (prevEnd >= curr.Date)
                        {
                            yield return new ValidationResult(
                                $"Die Datumsperioden überschneiden sich: {prev.Date:dd.MM.yyyy}–{prevEnd:dd.MM.yyyy} und {curr.Date:dd.MM.yyyy}–{(curr.EndDate ?? curr.Date):dd.MM.yyyy}.",
                                new[] { nameof(CalendarEntries) }
                            );
                            yield break;
                        }
                    }
                }
            }

            // At least one type must be active if no direct menu is set
            if (!MenuId.HasValue && !IsTimeTableBased && !IsCalendarBased)
            {
                yield return new ValidationResult(
                    $"Es muss entweder ein direktes Menü oder Zeitplan-/Kalender-basierte Einträge geben.",
                    new[] { nameof(MenuId) }
                );
            }
        }
    }
}
