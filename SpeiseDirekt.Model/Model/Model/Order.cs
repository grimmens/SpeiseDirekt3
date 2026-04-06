using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    /// <summary>
    /// A POS customer order (restaurant transaction).
    /// Not related to TenantSubscription (app feature billing).
    /// </summary>
    public class Order : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(20)]
        public string OrderNumber { get; set; } = string.Empty;

        [ForeignKey(nameof(Menu))]
        [Required]
        public Guid MenuId { get; set; }
        public Menu? Menu { get; set; }

        public OrderStatus Status { get; set; } = OrderStatus.Draft;

        [StringLength(500)]
        public string? Notes { get; set; }

        [Precision(18, 2)]
        public decimal SubTotal { get; set; }

        [Precision(18, 2)]
        public decimal TaxAmount { get; set; }

        [Precision(18, 2)]
        public decimal DiscountAmount { get; set; }

        [Precision(18, 2)]
        public decimal GrandTotal { get; set; }

        public PosPaymentMethod PaymentMethod { get; set; } = PosPaymentMethod.Cash;

        [ForeignKey(nameof(Discount))]
        public Guid? DiscountId { get; set; }
        public Discount? Discount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelledAt { get; set; }

        [StringLength(500)]
        public string? CancellationReason { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

        public Guid ApplicationUserId { get; set; }
    }
}
