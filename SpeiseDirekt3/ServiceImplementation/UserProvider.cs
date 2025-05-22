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
            if (_accessor != null && _accessor.HttpContext != null)
            {
                var identity = _accessor.HttpContext.User.Identity;
                if (identity.IsAuthenticated)
                {
                    return _accessor.HttpContext.User
                        .FindFirst(ClaimTypes.NameIdentifier).Value.ToString();
                }
            }
            return Guid.Empty.ToString();
        }
    }
}
