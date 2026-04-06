using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    // Database entity for storing translations
    public class TranslationCache
    {
        [Key]
        [StringLength(64)]
        public string Id { get; set; } = string.Empty; // Hash of source text + source lang + target lang

        [Required]
        [StringLength(4000)]
        public string SourceText { get; set; } = string.Empty;

        [Required]
        [StringLength(4000)]
        public string TranslatedText { get; set; } = string.Empty;

        [Required]
        public MenuLanguage SourceLanguage { get; set; }

        [Required]
        public MenuLanguage TargetLanguage { get; set; }
        [Required]

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [Required]

        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        public int UsageCount { get; set; } = 1;
    }
}
