namespace SpeiseDirekt.Model
{
    /// <summary>
    /// Payment method for POS orders (restaurant transactions).
    /// Not related to TenantSubscription (app feature billing).
    /// </summary>
    public enum PosPaymentMethod
    {
        Cash = 0,
        Card = 1
    }
}
