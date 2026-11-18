using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    public class Menu : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required(ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Name_Required))]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Menu_DescriptionRequired))]
        public string Description { get; set; } = string.Empty;

        public DesignTheme Theme { get; set; } = DesignTheme.Modern;
        public ICollection<Category>? Categories { get; set; }
        public ICollection<Allergen>? Allergens { get; set; }
        public Guid ApplicationUserId { get; set; }
        public MenuLanguage Language { get; set; } = MenuLanguage.German;

        /// <summary>
        /// Language versions: menus with the same group id are the same menu in different languages (at most one menu
        /// per <see cref="Language"/> in a group, unique index). <c>null</c> = not linked. Managed by
        /// <see cref="ServiceInterface.IMenuTranslationService"/>; read-only in the API.
        /// </summary>
        public Guid? TranslationGroupId { get; set; }
    }
}
