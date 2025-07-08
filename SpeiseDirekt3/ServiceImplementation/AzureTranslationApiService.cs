using Azure;
using Azure.AI.Translation.Text;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    // Example implementation for Azure Translator (you would need to install Azure.AI.Translation.Text package)
    
    public class AzureTranslationApiService : ITranslationApiService
    {
        private readonly TextTranslationClient _client;

        public AzureTranslationApiService(string apiKey, string region)
        {
            var credential = new AzureKeyCredential(apiKey);
            _client = new TextTranslationClient(credential, region);
        }

        public async Task<string> TranslateAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null)
        {
            var response = await _client.TranslateAsync(targetLanguageCode, text, sourceLanguageCode);
            return response.Value.FirstOrDefault()?.Translations?.FirstOrDefault()?.Text ?? text;
        }
    }
    
}
