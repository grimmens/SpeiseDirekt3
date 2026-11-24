using System.Net;
using System.Security.Cryptography;
using System.Text;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation
{
    /// <summary>
    /// <see cref="IVisitorKeyService"/> as a singleton: one random 32-byte salt in memory, replaced by a new one as soon as
    /// the local date of <see cref="TimeProvider.GetLocalNow"/> differs from the date it was made on (i.e. at local
    /// midnight, and after every restart). Nothing is written anywhere.
    /// </summary>
    public sealed class VisitorKeyService(TimeProvider clock) : IVisitorKeyService
    {
        private readonly object _gate = new();
        private DateOnly _saltDay;
        private byte[]? _salt;

        public string KeyFor(Guid tenantId, IPAddress? clientAddress, string? userAgent)
        {
            // An IPv4 client seen through a dual-stack socket counts as the same IPv4 address
            var address = clientAddress is { IsIPv4MappedToIPv6: true } ? clientAddress.MapToIPv4() : clientAddress;
            // Line breaks cannot occur in any of the three parts (a GUID, an address, an HTTP header value)
            var input = Encoding.UTF8.GetBytes($"{tenantId:N}\n{address}\n{userAgent}");
            var hash = HMACSHA256.HashData(CurrentSalt(), input);
            return Convert.ToHexStringLower(hash.AsSpan(0, IVisitorKeyService.KeyLength / 2));
        }

        private byte[] CurrentSalt()
        {
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            lock (_gate)
            {
                if (_salt is null || _saltDay != today)
                {
                    _salt = RandomNumberGenerator.GetBytes(32);
                    _saltDay = today;
                }
                return _salt;
            }
        }
    }
}
