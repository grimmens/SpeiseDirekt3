using SpeiseDirekt3.Model;

namespace SpeiseDirekt3.ServiceInterface
{
    public interface IAutomaticTranslator
    {
        /// <summary>
        /// Translates a MenuItem to the specified target language
        /// </summary>
        /// <param name="menuItem">The MenuItem to translate</param>
        /// <param name="targetLanguage">The target language for translation</param>
        /// <param name="sourceLanguage">The source language (optional, will be detected if not provided)</param>
        /// <returns>A new MenuItem with translated content</returns>
        Task<MenuItem> TranslateMenuItemAsync(MenuItem menuItem, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null);

        /// <summary>
        /// Translates a Category to the specified target language
        /// </summary>
        /// <param name="category">The Category to translate</param>
        /// <param name="targetLanguage">The target language for translation</param>
        /// <param name="sourceLanguage">The source language (optional, will be detected if not provided)</param>
        /// <returns>A new Category with translated content</returns>
        Task<Category> TranslateCategoryAsync(Category category, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null);

        /// <summary>
        /// Translates a collection of MenuItems to the specified target language
        /// </summary>
        /// <param name="menuItems">The MenuItems to translate</param>
        /// <param name="targetLanguage">The target language for translation</param>
        /// <param name="sourceLanguage">The source language (optional, will be detected if not provided)</param>
        /// <returns>A collection of new MenuItems with translated content</returns>
        Task<IEnumerable<MenuItem>> TranslateMenuItemsAsync(IEnumerable<MenuItem> menuItems, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null);

        /// <summary>
        /// Translates a collection of Categories to the specified target language
        /// </summary>
        /// <param name="categories">The Categories to translate</param>
        /// <param name="targetLanguage">The target language for translation</param>
        /// <param name="sourceLanguage">The source language (optional, will be detected if not provided)</param>
        /// <returns>A collection of new Categories with translated content</returns>
        Task<IEnumerable<Category>> TranslateCategoriesAsync(IEnumerable<Category> categories, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null);

        /// <summary>
        /// Translates a single text string to the specified target language
        /// </summary>
        /// <param name="text">The text to translate</param>
        /// <param name="targetLanguage">The target language for translation</param>
        /// <param name="sourceLanguage">The source language (optional, will be detected if not provided)</param>
        /// <returns>The translated text</returns>
        Task<string> TranslateTextAsync(string text, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null);

        /// <summary>
        /// Gets the list of supported languages for translation
        /// </summary>
        /// <returns>A collection of supported MenuLanguages</returns>
        IEnumerable<MenuLanguage> GetSupportedLanguages();

        /// <summary>
        /// Checks if a specific language is supported for translation
        /// </summary>
        /// <param name="language">The language to check</param>
        /// <returns>True if the language is supported, false otherwise</returns>
        bool IsLanguageSupported(MenuLanguage language);
    }
}
