using Microsoft.AspNetCore.Identity;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public TenantSubscription? TenantSubscription { get; set; }
    }

}
