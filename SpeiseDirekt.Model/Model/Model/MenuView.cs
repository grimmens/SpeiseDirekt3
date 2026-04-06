using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    // Tracking entity for analytics
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
}
