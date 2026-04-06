namespace SpeiseDirekt.Model
{
    /// <summary>
    /// Payment status for POS transactions (restaurant orders).
    /// Not related to TenantSubscription (app feature billing).
    /// </summary>
    public enum PosPaymentStatus
    {
        Pending = 0,
        Succeeded = 1,
        Failed = 2,
        Refunded = 3,
        PartiallyRefunded = 4
    }
}
