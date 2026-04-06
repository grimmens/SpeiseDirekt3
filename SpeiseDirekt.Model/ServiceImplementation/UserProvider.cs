using SpeiseDirekt.ServiceInterface;
using System.Security.Claims;

namespace SpeiseDirekt.ServiceImplementation
{
    public class UserIdProvider : IUserIdProvider
    {
        private readonly IHttpContextAccessor _accessor;

        public UserIdProvider(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public string GetUserId()
        {
            try
            {
                if (_accessor?.HttpContext?.User?.Identity?.IsAuthenticated == true)
                {
                    // For sub-accounts, return the tenant owner's ID so query filters work
                    var tenantOwnerId = _accessor.HttpContext.User.FindFirstValue("TenantOwnerId");
                    if (!string.IsNullOrEmpty(tenantOwnerId))
                    {
                        return tenantOwnerId;
                    }

                    // Owner account: return own ID
                    var userIdClaim = _accessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && !string.IsNullOrEmpty(userIdClaim.Value))
                    {
                        return userIdClaim.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user ID: {ex.Message}");
            }

            return Guid.Empty.ToString();
        }

        public string GetActualUserId()
        {
            try
            {
                if (_accessor?.HttpContext?.User?.Identity?.IsAuthenticated == true)
                {
                    var userIdClaim = _accessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && !string.IsNullOrEmpty(userIdClaim.Value))
                    {
                        return userIdClaim.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting actual user ID: {ex.Message}");
            }

            return Guid.Empty.ToString();
        }
    }
}
