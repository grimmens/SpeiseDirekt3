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

    }

    public class Category : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
        public string Name { get; set; }
   
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
        public bool ComplexNavigation { get; set; }
        public ICollection<Category>? Categories { get; set; }
        public Guid ApplicationUserId { get; set; }
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

    public class QRCode : IAppUserEntity, IValidatableObject
    {
        public Guid Id { get; set; }

        [Required, StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [ForeignKey(nameof(Menu))]
        public Guid? MenuId { get; set; }
        public Menu? Menu { get; set; }
        public bool IsTimeTableBased { get; set; }
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
        public List<TimeTableEntry>? TimeTableEntries { get; set; } = new List<TimeTableEntry>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid ApplicationUserId { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // If not timetable-based, nothing to validate here
            if (!IsTimeTableBased)
                yield break;
            if(IsTimeTableBased && (TimeTableEntries == null || !TimeTableEntries.Any()))
            {
                yield return new ValidationResult(
                        $"Es muss zumindest einen Eintrag geben.",
                        new[] { nameof(TimeTableEntries) }
                    );
            }

            // 1) Every entry must have a MenuId
            foreach (var entry in TimeTableEntries)
            {
                if (entry.MenuId == Guid.Empty)
                {
                    yield return new ValidationResult(
                        $"Ein Zeitplan‐Eintrag muss ein Menü auswählen.",
                        new[] { nameof(TimeTableEntries) }
                    );
                    // break early so we don't flood with one error per entry
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