using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    // Tracking entity for analytics
    public class MenuView
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The guest's daily visitor key (<c>IVisitorKeyService</c>: 16 hex characters, new every day and after a
        /// restart). Rows from before AP15a hold the random GUID of the former session cookie. Nothing else about the
        /// guest is stored: no IP address, no user agent (columns dropped by <c>RemoveGuestTrackingIpAndUserAgent</c>).
        /// </summary>
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
    }
}
