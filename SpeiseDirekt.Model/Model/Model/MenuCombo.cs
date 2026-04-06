using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class MenuCombo : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Precision(18, 2)]
        public decimal ComboPrice { get; set; }

        [ForeignKey(nameof(TriggerMenuItem))]
        [Required]
        public Guid TriggerMenuItemId { get; set; }
        public MenuItem? TriggerMenuItem { get; set; }

        [ForeignKey(nameof(Menu))]
        [Required]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }

        public ICollection<MenuComboItem> Items { get; set; } = new List<MenuComboItem>();

        public Guid ApplicationUserId { get; set; }
    }
}
