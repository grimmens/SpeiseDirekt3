using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace SpeiseDirekt.Model
{
    /// <summary>What a <see cref="MenuItemPriceOption"/> means. Serialized as "Variant" / "Extra" in the API.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PriceOptionKind>))]
    public enum PriceOptionKind
    {
        /// <summary>Alternative size or version with its own price ("0,25 l" € 3,40 / "0,5 l" € 5,90; "Standard" / "Vegan").</summary>
        Variant = 0,

        /// <summary>Optional add-on ordered on top of the item ("+ Halloumi" € 6,00).</summary>
        Extra = 1
    }

    /// <summary>
    /// A price variant or an extra of a menu item. When an item has at least one variant, its
    /// <see cref="MenuItem.Price"/> is kept equal to the lowest variant price
    /// (<see cref="Infrastructure.PriceOptions.ApplyLowestVariantPrice"/>), so ordering, combos and old clients keep a
    /// meaningful price. Deleted with the item (cascade).
    /// </summary>
    public class MenuItemPriceOption : IAppUserEntity
    {
        public const int NameMaxLength = 60;

        public Guid Id { get; set; }

        [ForeignKey(nameof(MenuItem))]
        public Guid MenuItemId { get; set; }

        [JsonIgnore]
        public MenuItem? MenuItem { get; set; }

        public PriceOptionKind Kind { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Name_RequiredShort))]
        [StringLength(NameMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.PriceOption_NameTooLong))]
        public string Name { get; set; } = string.Empty;

        [Precision(18, 2)]
        [Range(0d, 1_000_000d, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.PriceOption_PriceRange))]
        public decimal Price { get; set; }

        /// <summary>Position among the options of the same <see cref="Kind"/> (ascending).</summary>
        public int SortOrder { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
