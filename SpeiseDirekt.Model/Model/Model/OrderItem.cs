using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class OrderItem : IAppUserEntity
    {
        public Guid Id { get; set; }

        [ForeignKey(nameof(Order))]
        [Required]
        public Guid OrderId { get; set; }
        public Order? Order { get; set; }

        [ForeignKey(nameof(MenuItem))]
        [Required]
        public Guid MenuItemId { get; set; }
        public MenuItem? MenuItem { get; set; }

        [ForeignKey(nameof(MenuCombo))]
        public Guid? MenuComboId { get; set; }
        public MenuCombo? MenuCombo { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; } = 1;

        /// <summary>Snapshot of MenuItem.Price at order time.</summary>
        [Precision(18, 2)]
        public decimal UnitPrice { get; set; }

        /// <summary>Snapshot of MenuItem.Name at order time.</summary>
        [Required]
        [StringLength(200)]
        public string ItemName { get; set; } = string.Empty;

        /// <summary>Tax rate at order time, e.g. 0.2000 for 20%.</summary>
        [Precision(5, 4)]
        public decimal TaxRate { get; set; }

        /// <summary>LineTotal * TaxRate, rounded.</summary>
        [Precision(18, 2)]
        public decimal TaxAmount { get; set; }

        [Precision(18, 2)]
        public decimal DiscountAmount { get; set; }

        /// <summary>UnitPrice * Quantity.</summary>
        [Precision(18, 2)]
        public decimal LineTotal { get; set; }

        public bool IsComboItem { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
