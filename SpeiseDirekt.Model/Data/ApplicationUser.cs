using Microsoft.AspNetCore.Identity;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public TenantSubscription? TenantSubscription { get; set; }

        /// <summary>
        /// Null means this user IS the tenant owner. Set for sub-accounts.
        /// </summary>
        public string? TenantOwnerId { get; set; }
        public ApplicationUser? TenantOwner { get; set; }

        public ICollection<TenantUser>? TenantUsers { get; set; }
    }

}
