using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class CreateMenuItemDto : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        public List<Guid> AllergenIds { get; set; } = new();
        [Precision(18, 2)]
        public decimal Price { get; set; }
        [ForeignKey(nameof(Category))]
        public Guid CategoryId { get; set; }
        [Required]
        public Category? Category { get; set; }
        public Guid ApplicationUserId { get; set; }
        public string? ImagePath { get; set; }
    }
}
