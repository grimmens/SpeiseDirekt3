using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
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
}
