using Microsoft.Extensions.Caching.Memory;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <summary>
/// Server-side POS session service backed by IMemoryCache.
/// Cart state lives on the server; only the session GUID is stored on the client.
/// Sessions expire after 2 hours of inactivity.
/// </summary>
public class PosSessionService : IPosSessionService
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan SessionExpiration = TimeSpan.FromHours(2);

    public PosSessionService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Guid CreateSessionId()
    {
        var sessionId = Guid.NewGuid();
        _cache.Set(CacheKey(sessionId), new PosCart(), SessionExpiration);
        return sessionId;
    }

    public PosCart GetCart(Guid sessionId)
    {
        return _cache.GetOrCreate(CacheKey(sessionId), entry =>
        {
            entry.SlidingExpiration = SessionExpiration;
            return new PosCart();
        })!;
    }

    public void SaveCart(Guid sessionId, PosCart cart)
    {
        _cache.Set(CacheKey(sessionId), cart, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SessionExpiration
        });
    }

    public void ClearCart(Guid sessionId)
    {
        _cache.Set(CacheKey(sessionId), new PosCart(), new MemoryCacheEntryOptions
        {
            SlidingExpiration = SessionExpiration
        });
    }

    private static string CacheKey(Guid sessionId) => $"pos_session_{sessionId}";
}
