using System.Net;

namespace SpeiseDirekt.ServiceInterface
{
    /// <summary>
    /// The pseudonymous <b>daily visitor key</b> of a guest, stored as <c>MenuView.SessionId</c> /
    /// <c>MenuItemClick.SessionId</c> (AP15a: no cookie, no IP address, no user agent in the database). Built the way
    /// cookieless analytics do it: HMAC-SHA256 over the tenant (the menu's owner — one guest is never linked across two
    /// restaurants), the client address and the <c>User-Agent</c>, keyed with a random salt that lives only in memory
    /// and is replaced at local midnight (<see cref="TimeProvider"/>). The address and the user agent are used for this
    /// one computation only, never stored or logged. After a restart or at midnight the same guest gets a new key; that
    /// is the point.
    /// </summary>
    public interface IVisitorKeyService
    {
        /// <summary>
        /// The key of a visitor of <paramref name="tenantId"/> today: <see cref="KeyLength"/> lower-case hex characters.
        /// The same tenant, address and user agent give the same key until the salt is replaced.
        /// </summary>
        string KeyFor(Guid tenantId, IPAddress? clientAddress, string? userAgent);

        /// <summary>Length of a key in hex characters (64 bits of the HMAC).</summary>
        const int KeyLength = 16;
    }
}
