using System.Security.Claims;

namespace SpeiseDirekt.ServiceInterface
{
    public interface IUserIdProvider
    {
        string GetUserId();
    }
}
