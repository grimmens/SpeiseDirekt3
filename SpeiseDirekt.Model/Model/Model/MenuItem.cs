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
        /// <summary>Optional (e.g. drinks); never null, empty when not set.</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Position within the category (ascending, ties by name). New items get max + 1.</summary>
        public int SortOrder { get; set; }
        public ICollection<Allergen> Allergens { get; set; } = new List<Allergen>();
        [Precision(18,2)]
        public decimal Price { get; set; }
        [ForeignKey(nameof(Category))]
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid ApplicationUserId { get; set; }
        public string? ImagePath { get; set; }

        /// <summary>
        /// Labels shown to guests (badges on the menu card). The list of all labels, their texts and display order
        /// lives in <see cref="Infrastructure.ItemLabels"/>.
        /// </summary>
        public bool IsVegan { get; set; }
        public bool IsVegetarian { get; set; }
        public bool IsSpicy { get; set; }
        public bool IsHalal { get; set; }
        public bool IsAlcoholFree { get; set; }
        public bool IsNew { get; set; }
        public bool IsPopular { get; set; }

        /// <summary>
        /// Price variants and extras (<see cref="PriceOptionKind"/>). With at least one variant, <see cref="Price"/>
        /// equals the lowest variant price (<see cref="Infrastructure.PriceOptions"/>).
        /// </summary>
        public ICollection<MenuItemPriceOption> PriceOptions { get; set; } = new List<MenuItemPriceOption>();
    }
}
