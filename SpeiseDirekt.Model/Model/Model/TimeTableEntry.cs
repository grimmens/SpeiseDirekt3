using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
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
}
