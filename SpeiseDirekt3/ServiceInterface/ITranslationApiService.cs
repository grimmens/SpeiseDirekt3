namespace SpeiseDirekt3.ServiceInterface
{
    public interface ITranslationApiService
    {
        /// <summary>
        /// Translates text using an external translation service
        /// </summary>
        /// <param name="text">Text to translate</param>
        /// <param name="targetLanguageCode">Target language code (e.g., "en", "de", "fr")</param>
        /// <param name="sourceLanguageCode">Source language code (optional, auto-detect if null)</param>
        /// <returns>Translated text</returns>
        Task<string> TranslateAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null);
    }
}
