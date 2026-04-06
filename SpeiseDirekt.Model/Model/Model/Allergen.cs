using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class Allergen : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required, StringLength(5)]
        public string Code { get; set; } = string.Empty;
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;
        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
        [ForeignKey(nameof(Menu))]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }
        public Guid ApplicationUserId { get; set; }
    }
}
