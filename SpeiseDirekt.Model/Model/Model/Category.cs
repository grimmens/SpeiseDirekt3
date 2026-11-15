using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class Category : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional subtitle shown under the category name, e.g. "Mezze" or "served until 22:00".</summary>
        [StringLength(DescriptionMaxLength, ErrorMessage = "The subtitle can have at most 200 characters.")]
        public string? Description { get; set; }

        public const int DescriptionMaxLength = 200;

        /// <summary>Position within the menu (ascending, ties by name). New categories get max + 1.</summary>
        public int SortOrder { get; set; }

        public ICollection<MenuItem>? MenuItems { get; set; }
        [ForeignKey(nameof(Menu))]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }
        public Guid ApplicationUserId { get; set; }
    }
}
