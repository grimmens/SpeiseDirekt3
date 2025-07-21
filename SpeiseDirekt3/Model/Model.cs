using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt3.Data;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt3.Model
{

    public interface IAppUserEntity
    {
        Guid ApplicationUserId { get; set; }
    }
    public class MenuItem : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        public string Allergens { get; set; } = string.Empty;
        [Precision(18,2)]
        public decimal Price { get; set; }
        [ForeignKey(nameof(Category))]
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid ApplicationUserId { get; set; }
        public string? ImagePath { get; set; }

    }

    public class Category : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
        public string Name { get; set; } = string.Empty;
   
        public ICollection<MenuItem>? MenuItems { get; set; }
        [ForeignKey(nameof(Menu))]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }
        public Guid ApplicationUserId { get; set; }
    }

    public enum DesignTheme
    {
        Elegant,
        Modern,
        Classic,
        Minimal,
        Fancy,
        Dark
    }

    public class Menu : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        public DesignTheme Theme { get; set; } = DesignTheme.Modern;
        public ICollection<Category>? Categories { get; set; }
        public Guid ApplicationUserId { get; set; }
        public MenuLanguage Language { get; set; } = MenuLanguage.German;
    }

    public enum MenuLanguage
    {
        German,
        English,
        French,
        Spanish,
        Italian,
        Dutch,
        Portuguese,
        Polish,
        Czech,
        Hungarian,
        Croatian,
        Slovenian,
        Romanian,
        Bulgarian,
        Greek,
        Russian,
        Turkish,
        Arabic,
        Chinese,
        Japanese,
        Korean
    }

    public class TimeTableEntry : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        public TimeOnly? StartTime { get; set; }

        [Required]
        public TimeOnly? EndTime { get; set; }

        [ForeignKey(nameof(Menu))]
        [Required]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }

        [ForeignKey(nameof(QRCode))]
        [Required]
        public Guid QRCodeId { get; set; }
        public QRCode? QRCode { get; set; }
        public Guid ApplicationUserId { get; set; }
    }

    public class CalendarEntry : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        public DateOnly Date { get; set; }

        // Optional: Wochentag-basierte Wiederholung
        public DayOfWeek? RecurringDayOfWeek { get; set; }

        // Optional: Datumsbereich für Events
        public DateOnly? EndDate { get; set; }

        [ForeignKey(nameof(Menu))]
        [Required]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }

        [ForeignKey(nameof(QRCode))]
        [Required]
        public Guid QRCodeId { get; set; }
        public QRCode? QRCode { get; set; }
        public Guid ApplicationUserId { get; set; }
    }

    // Database entity for storing translations
    public class TranslationCache
    {
        [Key]
        [StringLength(64)]
        public string Id { get; set; } = string.Empty; // Hash of source text + source lang + target lang

        [Required]
        [StringLength(4000)]
        public string SourceText { get; set; } = string.Empty;

        [Required]
        [StringLength(4000)]
        public string TranslatedText { get; set; } = string.Empty;

        [Required]
        public MenuLanguage SourceLanguage { get; set; }

        [Required]
        public MenuLanguage TargetLanguage { get; set; }
        [Required]

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [Required]

        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        public int UsageCount { get; set; } = 1;
    }

    // Tracking entities for analytics
    public class MenuView
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(128)]
        public string SessionId { get; set; } = string.Empty;

        [ForeignKey(nameof(Menu))]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }

        [ForeignKey(nameof(QRCode))]
        public Guid? QRCodeId { get; set; }
        public QRCode? QRCode { get; set; }

        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;

        [StringLength(45)] // IPv6 max length
        public string? IpAddress { get; set; }

        [StringLength(500)]
        public string? UserAgent { get; set; }
    }

    public class MenuItemClick
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(128)]
        public string SessionId { get; set; } = string.Empty;

        [ForeignKey(nameof(MenuItem))]
        public Guid MenuItemId { get; set; }
        public MenuItem? MenuItem { get; set; }

        [ForeignKey(nameof(Menu))]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }

        public DateTime ClickedAt { get; set; } = DateTime.UtcNow;

        [StringLength(45)] // IPv6 max length
        public string? IpAddress { get; set; }

        [StringLength(500)]
        public string? UserAgent { get; set; }
    }

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

    public class Image
    {
        public Guid Id { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string MimeType { get; set; } = string.Empty;
    }

    public class TenantSubscription
    {
        [Key]
        public string TenantId { get; set; } = default!;
        public ApplicationUser? Tenant { get; set; } = default!;
        public bool IsPaid { get; set; } = false;
        public DateTime SubscriptionStart { get; set; } = DateTime.UtcNow;
        public DateTime? SubscriptionEnd { get; set; }
    }

}