using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    public class Discount : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public DiscountType Type { get; set; }

        /// <summary>Percentage (0-100) or fixed amount.</summary>
        [Precision(18, 2)]
        public decimal Value { get; set; }

        [Precision(18, 2)]
        public decimal? MinOrderAmount { get; set; }

        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }

        public int? MaxUses { get; set; }

        [ConcurrencyCheck]
        public int CurrentUses { get; set; }

        public bool IsActive { get; set; } = true;

        public Guid ApplicationUserId { get; set; }
    }
}
