using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
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
}
