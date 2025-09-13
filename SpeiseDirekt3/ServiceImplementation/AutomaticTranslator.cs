using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class AutomaticTranslator : IAutomaticTranslator
    {
        private readonly ITranslationApiService _translationApiService;
        private readonly ILogger<AutomaticTranslator> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        // Batch processing to reduce API calls and database queries
        private readonly int _maxBatchSize = 100;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

        // Connection pooling and parallel processing limits
        private readonly SemaphoreSlim _apiSemaphore = new(10); // Limit concurrent API calls
        private readonly SemaphoreSlim _dbSemaphore = new(50);  // Limit concurrent DB operations

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
                // Batch translate all text fields at once
                var textsToTranslate = new List<string>
                {
                    menuItem.Name ?? string.Empty,
                    menuItem.Description ?? string.Empty,
                    menuItem.Allergens ?? string.Empty
                }.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

                var translatedTexts = await TranslateTextsAsync(textsToTranslate, targetLanguage, sourceLanguage);

                var translatedMenuItem = new MenuItem
                {
                    Id = Guid.NewGuid(),
                    Name = translatedTexts.GetValueOrDefault(menuItem.Name) ?? menuItem.Name,
                    Description = translatedTexts.GetValueOrDefault(menuItem.Description) ?? menuItem.Description,
                    Allergens = translatedTexts.GetValueOrDefault(menuItem.Allergens) ?? menuItem.Allergens,
                    Price = menuItem.Price,
                    CategoryId = menuItem.CategoryId,
                    ApplicationUserId = menuItem.ApplicationUserId,
                    ImagePath = menuItem.ImagePath
                };

                return translatedMenuItem;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate MenuItem '{Name}'", menuItem.Name);
                throw;
            }
        }

        public async Task<Category> TranslateCategoryAsync(Category category, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));

            try
            {
                var translatedName = await TranslateTextAsync(category.Name, targetLanguage, sourceLanguage);

                return new Category
                {
                    Id = Guid.NewGuid(),
                    Name = translatedName,
                    MenuId = category.MenuId,
                    ApplicationUserId = category.ApplicationUserId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate Category '{Name}'", category.Name);
                throw;
            }
        }

        public async Task<IEnumerable<MenuItem>> TranslateMenuItemsAsync(IEnumerable<MenuItem> menuItems, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (menuItems == null) throw new ArgumentNullException(nameof(menuItems));

            var menuItemsList = menuItems.ToList();
            if (!menuItemsList.Any()) return Enumerable.Empty<MenuItem>();

            // Collect all unique texts that need translation
            var uniqueTexts = new HashSet<string>();
            foreach (var item in menuItemsList)
            {
                if (!string.IsNullOrWhiteSpace(item.Name)) uniqueTexts.Add(item.Name);
                if (!string.IsNullOrWhiteSpace(item.Description)) uniqueTexts.Add(item.Description);
                if (!string.IsNullOrWhiteSpace(item.Allergens)) uniqueTexts.Add(item.Allergens);
            }

            // Batch translate all unique texts
            var translations = await TranslateTextsAsync(uniqueTexts, targetLanguage, sourceLanguage);

            // Apply translations to all menu items
            var translatedItems = new List<MenuItem>();
            foreach (var menuItem in menuItemsList)
            {
                var translatedMenuItem = new MenuItem
                {
                    Id = Guid.NewGuid(),
                    Name = translations.GetValueOrDefault(menuItem.Name) ?? menuItem.Name,
                    Description = translations.GetValueOrDefault(menuItem.Description) ?? menuItem.Description,
                    Allergens = translations.GetValueOrDefault(menuItem.Allergens) ?? menuItem.Allergens,
                    Price = menuItem.Price,
                    CategoryId = menuItem.CategoryId,
                    ApplicationUserId = menuItem.ApplicationUserId,
                    ImagePath = menuItem.ImagePath
                };
                translatedItems.Add(translatedMenuItem);
            }

            return translatedItems;
        }

        public async Task<IEnumerable<Category>> TranslateCategoriesAsync(IEnumerable<Category> categories, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (categories == null) throw new ArgumentNullException(nameof(categories));

            var categoriesList = categories.ToList();
            if (!categoriesList.Any()) return Enumerable.Empty<Category>();

            // Collect all category names for batch translation
            var categoryNames = categoriesList
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name)
                .Distinct()
                .ToList();

            var translations = await TranslateTextsAsync(categoryNames, targetLanguage, sourceLanguage);

            // Apply translations
            var translatedCategories = categoriesList.Select(category => new Category
            {
                Id = Guid.NewGuid(),
                Name = translations.GetValueOrDefault(category.Name) ?? category.Name,
                MenuId = category.MenuId,
                ApplicationUserId = category.ApplicationUserId
            }).ToList();

            return translatedCategories;
        }

        // NEW: Batch translation method - the key performance improvement
        public async Task<Dictionary<string, string>> TranslateTextsAsync(IEnumerable<string> texts, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            var textsList = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            if (!textsList.Any()) return new Dictionary<string, string>();

            var effectiveSourceLanguage = sourceLanguage ?? MenuLanguage.German;
            if (effectiveSourceLanguage == targetLanguage)
            {
                return textsList.ToDictionary(t => t, t => t);
            }

            var results = new ConcurrentDictionary<string, string>();

            // Step 1: Check memory cache for all texts
            var uncachedTexts = new List<string>();
            foreach (var text in textsList)
            {
                var cacheKey = GenerateCacheKey(text, effectiveSourceLanguage, targetLanguage);
                if (_memoryCache.TryGetValue(cacheKey, out string? cachedTranslation) && !string.IsNullOrEmpty(cachedTranslation))
                {
                    results[text] = cachedTranslation;
                }
                else
                {
                    uncachedTexts.Add(text);
                }
            }

            if (!uncachedTexts.Any()) return results.ToDictionary();

            // Step 2: Batch check database cache
            var dbTranslations = await GetTranslationsFromDatabaseAsync(uncachedTexts, effectiveSourceLanguage, targetLanguage);

            var textsNeedingApiTranslation = new List<string>();
            foreach (var text in uncachedTexts)
            {
                if (dbTranslations.TryGetValue(text, out var dbTranslation))
                {
                    results[text] = dbTranslation;
                    // Cache in memory for next time
                    var cacheKey = GenerateCacheKey(text, effectiveSourceLanguage, targetLanguage);
                    _memoryCache.Set(cacheKey, dbTranslation, _cacheExpiration);
                }
                else
                {
                    textsNeedingApiTranslation.Add(text);
                }
            }

            if (!textsNeedingApiTranslation.Any()) return results.ToDictionary();

            // Step 3: Batch API translation with concurrency control
            await ProcessApiTranslationsAsync(textsNeedingApiTranslation, effectiveSourceLanguage, targetLanguage, results);

            return results.ToDictionary();
        }

        private async Task ProcessApiTranslationsAsync(List<string> texts, MenuLanguage sourceLanguage, MenuLanguage targetLanguage, ConcurrentDictionary<string, string> results)
        {
            var targetCode = GetLanguageCode(targetLanguage);
            var sourceCode = GetLanguageCode(sourceLanguage);

            // Process in batches to avoid overwhelming the API
            var batches = texts.Chunk(_maxBatchSize).ToList();

            var tasks = batches.Select(async batch =>
            {
                await _apiSemaphore.WaitAsync();
                try
                {
                    // If your translation service supports batch translation, use it here
                    // Otherwise, process the batch with controlled concurrency
                    var batchTasks = batch.Select(async text =>
                    {
                        try
                        {
                            var translatedText = await _translationApiService.TranslateAsync(text, targetCode, sourceCode);
                            results[text] = translatedText;

                            // Store in cache asynchronously without awaiting
                            _ = Task.Run(async () => await StoreTranslationAsync(text, translatedText, sourceLanguage, targetLanguage));

                            return (text, translatedText);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to translate text: {Text}", text);
                            results[text] = text; // Fallback to original text
                            return (text, text);
                        }
                    });

                    await Task.WhenAll(batchTasks);
                }
                finally
                {
                    _apiSemaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        // Optimized database operations
        private async Task<Dictionary<string, string>> GetTranslationsFromDatabaseAsync(List<string> texts, MenuLanguage sourceLanguage, MenuLanguage targetLanguage)
        {
            if (!texts.Any()) return new Dictionary<string, string>();

            await _dbSemaphore.WaitAsync();
            try
            {
                using var context = await _dbContextFactory.CreateDbContextAsync();

                var cacheKeys = texts.Select(text => GenerateCacheKey(text, sourceLanguage, targetLanguage)).ToList();

                var dbResults = await context.TranslationCaches
                    .Where(tc => cacheKeys.Contains(tc.Id))
                    .ToDictionaryAsync(tc => tc.SourceText, tc => tc.TranslatedText);

                // Update usage stats in background
                _ = Task.Run(async () => await UpdateUsageStatsAsync(cacheKeys));

                return dbResults;
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private async Task UpdateUsageStatsAsync(List<string> cacheKeys)
        {
            try
            {
                await _dbSemaphore.WaitAsync();
                using var context = await _dbContextFactory.CreateDbContextAsync();

                // Batch update usage stats
                await context.TranslationCaches
                    .Where(tc => cacheKeys.Contains(tc.Id))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(tc => tc.LastUsedAt, DateTime.UtcNow)
                        .SetProperty(tc => tc.UsageCount, tc => tc.UsageCount + 1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update translation usage stats");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task<string> TranslateTextAsync(string text, MenuLanguage targetLanguage, MenuLanguage? sourceLanguage = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var translations = await TranslateTextsAsync(new[] { text }, targetLanguage, sourceLanguage);
            return translations.GetValueOrDefault(text, text);
        }

        private async Task StoreTranslationAsync(string sourceText, string translatedText, MenuLanguage sourceLanguage, MenuLanguage targetLanguage)
        {
            try
            {
                var cacheKey = GenerateCacheKey(sourceText, sourceLanguage, targetLanguage);

                // Store in memory cache
                _memoryCache.Set(cacheKey, translatedText, _cacheExpiration);

                // Store in database - simple approach to avoid conflicts
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
            }
            catch (Exception ex)
            {
                // Ignore conflicts - translation already exists, which is fine
                if (!ex.Message.Contains("duplicate key") && !ex.Message.Contains("UNIQUE constraint"))
                {
                    _logger.LogError(ex, "Failed to store translation in database");
                }
            }
        }

        private string GenerateCacheKey(string text, MenuLanguage sourceLanguage, MenuLanguage targetLanguage)
        {
            var input = $"{text}|{sourceLanguage}|{targetLanguage}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public IEnumerable<MenuLanguage> GetSupportedLanguages() => _languageCodes.Keys;
        public bool IsLanguageSupported(MenuLanguage language) => _languageCodes.ContainsKey(language);

        private string GetLanguageCode(MenuLanguage language)
        {
            if (!_languageCodes.TryGetValue(language, out var code))
                throw new NotSupportedException($"Language {language} is not supported for translation.");
            return code;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _apiSemaphore?.Dispose();
                _dbSemaphore?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}