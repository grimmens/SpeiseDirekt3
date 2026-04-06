using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    /// <summary>
    /// Payment record for a POS order (restaurant transaction).
    /// Not related to TenantSubscription (app feature billing).
    /// </summary>
    public class PosPayment : IAppUserEntity
    {
        public Guid Id { get; set; }

        [ForeignKey(nameof(Order))]
        [Required]
        public Guid OrderId { get; set; }
        public Order? Order { get; set; }

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(3)]
        public string Currency { get; set; } = "EUR";

        public PosPaymentStatus Status { get; set; } = PosPaymentStatus.Pending;

        public PosPaymentMethod PaymentMethod { get; set; }

        [StringLength(500)]
        public string? StripeSessionId { get; set; }

        [StringLength(500)]
        public string? StripePaymentIntentId { get; set; }

        [StringLength(500)]
        public string? StripeRefundId { get; set; }

        [Precision(18, 2)]
        public decimal? RefundAmount { get; set; }

        [StringLength(500)]
        public string? RefundReason { get; set; }

        [Required]
        [StringLength(100)]
        public string IdempotencyKey { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        [StringLength(1000)]
        public string? FailureReason { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
