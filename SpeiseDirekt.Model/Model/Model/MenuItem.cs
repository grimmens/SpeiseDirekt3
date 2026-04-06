using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class MenuItem : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        public ICollection<Allergen> Allergens { get; set; } = new List<Allergen>();
        [Precision(18,2)]
        public decimal Price { get; set; }
        [ForeignKey(nameof(Category))]
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid ApplicationUserId { get; set; }
        public string? ImagePath { get; set; }
    }
}
