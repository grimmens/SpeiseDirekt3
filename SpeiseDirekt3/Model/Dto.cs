using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt3.Model
{
    public class CreateMenuItemDto : IAppUserEntity
    {
        public Guid Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        public string Allergens { get; set; } = string.Empty;
        [Precision(18, 2)]
        public decimal Price { get; set; }
        [ForeignKey(nameof(Category))]
        public Guid CategoryId { get; set; }
        [Required]
        public Category? Category { get; set; }
        public Guid ApplicationUserId { get; set; }
        public string? ImagePath { get; set; }

    }


    public class BreadcrumbItem
    {
        public string Title { get; set; }
        public string Link { get; set; }

        public BreadcrumbItem(string title, string link)
        {
            Title = title;
            Link = link;
        }
    }

}
