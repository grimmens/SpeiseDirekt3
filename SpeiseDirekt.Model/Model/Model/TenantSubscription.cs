using SpeiseDirekt.Data;
using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    public class TenantSubscription
    {
        [Key]
        public string TenantId { get; set; } = default!;
        public ApplicationUser? Tenant { get; set; } = default!;
        public bool IsPaid { get; set; } = false;
        public DateTime SubscriptionStart { get; set; } = DateTime.UtcNow;
        public DateTime? SubscriptionEnd { get; set; }
        public int MaxUsers { get; set; } = 1;

        [StringLength(50)]
        public string? PlanName { get; set; }
    }
}
