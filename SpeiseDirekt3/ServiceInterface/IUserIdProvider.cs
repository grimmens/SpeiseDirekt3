using System.Security.Claims;

namespace SpeiseDirekt3.ServiceInterface
{
    public interface IUserIdProvider
    {
        string GetUserId();
    }
}
