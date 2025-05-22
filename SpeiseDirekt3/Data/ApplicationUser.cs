using Microsoft.AspNetCore.Identity;
using SpeiseDirekt3.Model;

namespace SpeiseDirekt3.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public TenantSubscription? TenantSubscription { get; set; }
    }

}
