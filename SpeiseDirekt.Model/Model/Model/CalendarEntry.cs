using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
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
}
