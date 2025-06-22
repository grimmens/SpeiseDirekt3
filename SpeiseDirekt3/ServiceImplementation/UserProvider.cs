using SpeiseDirekt3.ServiceInterface;
using System.Security.Claims;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class UserIdProvider : IUserIdProvider
    {
        private IHttpContextAccessor _accessor;
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
                    var userIdClaim = _accessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && !string.IsNullOrEmpty(userIdClaim.Value))
                    {
                        return userIdClaim.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                Console.WriteLine($"Error getting user ID: {ex.Message}");
            }

            return Guid.Empty.ToString();
        }
    }
}
