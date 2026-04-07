using Microsoft.Extensions.Caching.Memory;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class PosCustomerService : IPosCustomerService
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan SessionExpiration = TimeSpan.FromHours(2);

    public PosCustomerService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public PosCustomerSession? GetSession(string sessionId)
    {
        _cache.TryGetValue(CacheKey(sessionId), out PosCustomerSession? session);
        return session;
    }

    public void SaveSession(string sessionId, PosCustomerSession session)
    {
        _cache.Set(CacheKey(sessionId), session, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SessionExpiration
        });
    }

    public void ClearSession(string sessionId)
    {
        _cache.Remove(CacheKey(sessionId));
    }

    private static string CacheKey(string sessionId) => $"pos_customer_{sessionId}";
}
