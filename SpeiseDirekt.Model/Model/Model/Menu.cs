using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    public class Menu : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        public DesignTheme Theme { get; set; } = DesignTheme.Modern;
        public ICollection<Category>? Categories { get; set; }
        public ICollection<Allergen>? Allergens { get; set; }
        public Guid ApplicationUserId { get; set; }
        public MenuLanguage Language { get; set; } = MenuLanguage.German;
    }
}
