using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;
using System.Security.Cryptography;
using System.Text;

namespace SpeiseDirekt3.ServiceImplementation
{
    // Enhanced implementation with caching and database storage
    public class AutomaticTranslator : IAutomaticTranslator
    {
        private readonly ITranslationApiService _translationApiService;
        private readonly ILogger<AutomaticTranslator> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        // Cache expiration time for in-memory cache
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

        // Map MenuLanguage enum to language codes that translation services expect
        private readonly Dictionary<MenuLanguage, string> _languageCodes = new()
        {
            { MenuLanguage.German, "de" },
            { MenuLanguage.English, "en" },
            { MenuLanguage.French, "fr" },
            { MenuLanguage.Spanish, "es" },
            { MenuLanguage.Italian, "it" },
            { MenuLanguage.Dutch, "nl" },
            { MenuLanguage.Portuguese, "pt" },
            { MenuLanguage.Polish, "pl" },
            { MenuLanguage.Czech, "cs" },
            { MenuLanguage.Hungarian, "hu" },
            { MenuLanguage.Croatian, "hr" },
            { MenuLanguage.Slovenian, "sl" },
            { MenuLanguage.Romanian, "ro" },
            { MenuLanguage.Bulgarian, "bg" },
            { MenuLanguage.Greek, "el" },
            { MenuLanguage.Russian, "ru" },
            { MenuLanguage.Turkish, "tr" },
            { MenuLanguage.Arabic, "ar" },
            { MenuLanguage.Chinese, "zh" },
            { MenuLanguage.Japanese, "ja" },
            { MenuLanguage.Korean, "ko" }
        };

        public AutomaticTranslator(
            ITranslationApiService translationApiService,
            ILogger<AutomaticTranslator> logger,
            IMemoryCache memoryCache,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _translationApiService = translationApiService ?? throw new ArgumentNullException(nameof(translationApiService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        }

        public async Task<MenuItem> TranslateMenuItemAsync(MenuItem menuItem, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (menuItem == null) throw new ArgumentNullException(nameof(menuItem));

            try
            {
                var translatedMenuItem = new MenuItem
                {
                    Id = Guid.NewGuid(), // New ID for translated version
                    Name = await TranslateTextAsync(menuItem.Name, targetLanguage, sourceLanguage),
                    Description = await TranslateTextAsync(menuItem.Description, targetLanguage, sourceLanguage),
                    Allergens = await TranslateTextAsync(menuItem.Allergens, targetLanguage, sourceLanguage),
                    Price = menuItem.Price, // Price remains the same
                    CategoryId = menuItem.CategoryId, // Keep same category reference
                    ApplicationUserId = menuItem.ApplicationUserId, // Keep same user
                    ImagePath = menuItem.ImagePath // Keep same image
                };

                _logger.LogInformation("Successfully translated MenuItem '{Name}' to {TargetLanguage}", menuItem.Name, targetLanguage);
                return translatedMenuItem;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate MenuItem '{Name}' to {TargetLanguage}", menuItem.Name, targetLanguage);
                throw;
            }
        }

        public async Task<Category> TranslateCategoryAsync(Category category, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));

            try
            {
                var translatedCategory = new Category
                {
                    Id = Guid.NewGuid(), // New ID for translated version
                    Name = await TranslateTextAsync(category.Name, targetLanguage, sourceLanguage),
                    MenuId = category.MenuId, // Keep same menu reference
                    ApplicationUserId = category.ApplicationUserId // Keep same user
                    // Note: MenuItems collection would need to be translated separately if needed
                };

                _logger.LogInformation("Successfully translated Category '{Name}' to {TargetLanguage}", category.Name, targetLanguage);
                return translatedCategory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate Category '{Name}' to {TargetLanguage}", category.Name, targetLanguage);
                throw;
            }
        }

        public async Task<IEnumerable<MenuItem>> TranslateMenuItemsAsync(IEnumerable<MenuItem> menuItems, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (menuItems == null) throw new ArgumentNullException(nameof(menuItems));

            var translatedItems = new List<MenuItem>();

            foreach (var menuItem in menuItems)
            {
                var translatedItem = await TranslateMenuItemAsync(menuItem, targetLanguage, sourceLanguage);
                translatedItems.Add(translatedItem);
            }

            return translatedItems;
        }

        public async Task<IEnumerable<Category>> TranslateCategoriesAsync(IEnumerable<Category> categories, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (categories == null) throw new ArgumentNullException(nameof(categories));

            var translatedCategories = new List<Category>();

            foreach (var category in categories)
            {
                var translatedCategory = await TranslateCategoryAsync(category, targetLanguage, sourceLanguage);
                translatedCategories.Add(translatedCategory);
            }

            return translatedCategories;
        }

        public async Task<string> TranslateTextAsync(string text, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Default source language to German if not specified
            var effectiveSourceLanguage = sourceLanguage ?? MenuLanguage.German;

            // If source and target are the same, no translation needed
            if (effectiveSourceLanguage == targetLanguage) return text;

            try
            {
                // Step 1: Check in-memory cache first
                var cacheKey = GenerateCacheKey(text, effectiveSourceLanguage, targetLanguage);

                if (_memoryCache.TryGetValue(cacheKey, out string? cachedTranslation) && !string.IsNullOrEmpty(cachedTranslation))
                {
                    _logger.LogDebug("Translation found in memory cache for key: {CacheKey}", cacheKey);
                    await UpdateUsageStatsAsync(cacheKey);
                    return cachedTranslation;
                }

                // Step 2: Check database cache
                var dbTranslation = await GetTranslationFromDatabaseAsync(text, effectiveSourceLanguage, targetLanguage);
                if (dbTranslation != null)
                {
                    _logger.LogDebug("Translation found in database cache for: {Text}", text);

                    // Store in memory cache for faster access next time
                    _memoryCache.Set(cacheKey, dbTranslation.TranslatedText, _cacheExpiration);

                    await UpdateUsageStatsAsync(cacheKey, dbTranslation);
                    return dbTranslation.TranslatedText;
                }

                // Step 3: Call translation API service
                _logger.LogDebug("No cached translation found, calling translation API for: {Text}", text);

                var targetCode = GetLanguageCode(targetLanguage);
                var sourceCode = GetLanguageCode(effectiveSourceLanguage);

                var translatedText = await _translationApiService.TranslateAsync(text, targetCode, sourceCode);

                // Step 4: Store the translation in both caches
                await StoreTranslationAsync(text, translatedText, effectiveSourceLanguage, targetLanguage);

                _logger.LogInformation("Successfully translated and cached text from {SourceLang} to {TargetLang}",
                    effectiveSourceLanguage, targetLanguage);

                return translatedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate text to {TargetLanguage}", targetLanguage);
                throw;
            }
        }

        private string GenerateCacheKey(string text, MenuLanguage sourceLanguage, MenuLanguage targetLanguage)
        {
            // Create a consistent hash for the cache key
            var input = $"{text}|{sourceLanguage}|{targetLanguage}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Convert to hex string (64 characters) instead of base64 (88 characters)
            // This is more efficient for database storage and indexing
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private async Task<TranslationCache?> GetTranslationFromDatabaseAsync(string text, MenuLanguage sourceLanguage, MenuLanguage targetLanguage)
        {
            try
            {
                using var context = await _dbContextFactory.CreateDbContextAsync();
                var cacheKey = GenerateCacheKey(text, sourceLanguage, targetLanguage);

                return await context.TranslationCaches
                    .FirstOrDefaultAsync(tc => tc.Id == cacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve translation from database");
                return null;
            }
        }

        private async Task StoreTranslationAsync(string sourceText, string translatedText, MenuLanguage sourceLanguage, MenuLanguage targetLanguage)
        {
            try
            {
                var cacheKey = GenerateCacheKey(sourceText, sourceLanguage, targetLanguage);

                // Store in memory cache
                _memoryCache.Set(cacheKey, translatedText, _cacheExpiration);

                // Store in database
                using var context = await _dbContextFactory.CreateDbContextAsync();

                var translationCache = new TranslationCache
                {
                    Id = cacheKey,
                    SourceText = sourceText,
                    TranslatedText = translatedText,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    UsageCount = 1
                };

                context.TranslationCaches.Add(translationCache);
                await context.SaveChangesAsync();

                _logger.LogDebug("Stored translation in database with key: {CacheKey}", cacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store translation in database");
                // Don't throw here - translation was successful, just caching failed
            }
        }

        private async Task UpdateUsageStatsAsync(string cacheKey, TranslationCache? dbTranslation = null)
        {
            try
            {
                using var context = await _dbContextFactory.CreateDbContextAsync();

                var translation = dbTranslation ?? await context.TranslationCaches.FindAsync(cacheKey);
                if (translation != null)
                {
                    translation.LastUsedAt = DateTime.UtcNow;
                    translation.UsageCount++;

                    if (dbTranslation == null) // Only update if we fetched it ourselves
                    {
                        context.TranslationCaches.Update(translation);
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update translation usage stats");
                // Don't throw - this is just for statistics
            }
        }

        public IEnumerable<MenuLanguage> GetSupportedLanguages()
        {
            return _languageCodes.Keys;
        }

        public bool IsLanguageSupported(MenuLanguage language)
        {
            return _languageCodes.ContainsKey(language);
        }

        private string GetLanguageCode(MenuLanguage language)
        {
            if (!_languageCodes.TryGetValue(language, out var code))
            {
                throw new NotSupportedException($"Language {language} is not supported for translation.");
            }
            return code;
        }
    }
}
