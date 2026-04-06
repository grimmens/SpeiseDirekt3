namespace SpeiseDirekt.ServiceInterface
{
    public interface IUserIdProvider
    {
        /// <summary>
        /// Returns the tenant owner ID (for query filters). For sub-accounts, returns TenantOwnerId.
        /// </summary>
        string GetUserId();

        /// <summary>
        /// Returns the actual logged-in user's ID.
        /// </summary>
        string GetActualUserId();
    }
}
