using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class PosCustomerService : IPosCustomerService
{
    private readonly IMemoryCache _cache;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAddressService _addressService;
    private static readonly TimeSpan SessionExpiration = TimeSpan.FromHours(2);

    public PosCustomerService(
        IMemoryCache cache,
        UserManager<ApplicationUser> userManager,
        IAddressService addressService)
    {
        _cache = cache;
        _userManager = userManager;
        _addressService = addressService;
    }

    public PosCustomerSession? GetSessionRaw(string sessionId)
    {
        _cache.TryGetValue(CacheKey(sessionId), out PosCustomerSession? session);
        return session;
    }

    public async Task<PosCustomerSession> GetOrCreateSessionAsync(string sessionId, string? currentUserId)
    {
        var session = GetSessionRaw(sessionId);

        if (currentUserId == null)
        {
            return session ?? EnsureGuestSession(sessionId);
        }

        // Authenticated user: promote guest, or initialize fresh registered session.
        if (session is RegisteredUserPosSession registered &&
            registered.ApplicationUserId == currentUserId)
        {
            return registered;
        }

        if (session is GuestPosSession guest)
        {
            return await PromoteGuestToUserAsync(sessionId, guest, currentUserId);
        }

        // Either no session, or a stale registered session for a different user.
        return await InitializeForUserAsync(sessionId, currentUserId);
    }

    public GuestPosSession EnsureGuestSession(string sessionId)
    {
        if (GetSessionRaw(sessionId) is GuestPosSession existing)
            return existing;

        var guest = new GuestPosSession();
        Save(sessionId, guest);
        return guest;
    }

    public async Task<RegisteredUserPosSession> InitializeForUserAsync(string sessionId, string applicationUserId)
    {
        var user = await _userManager.FindByIdAsync(applicationUserId)
            ?? throw new InvalidOperationException($"User {applicationUserId} not found.");

        var session = new RegisteredUserPosSession
        {
            ApplicationUserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber,
        };

        var defaultAddress = (await _addressService.GetForUserAsync(user.Id)).FirstOrDefault();
        if (defaultAddress != null)
        {
            session.SelectedAddressId = defaultAddress.Id;
            session.DeliveryAddress = ToSessionAddress(defaultAddress);
        }

        Save(sessionId, session);
        return session;
    }

    public async Task<RegisteredUserPosSession?> SelectAddressAsync(string sessionId, Guid addressId)
    {
        if (GetSessionRaw(sessionId) is not RegisteredUserPosSession registered)
            return null;

        var address = await _addressService.GetByIdAsync(addressId, registered.ApplicationUserId);
        if (address == null) return null;

        registered.SelectedAddressId = address.Id;
        registered.DeliveryAddress = ToSessionAddress(address);
        Save(sessionId, registered);
        return registered;
    }

    public void UpdateContact(string sessionId, string? email, string? firstName, string? lastName, string? phone)
    {
        var session = GetSessionRaw(sessionId) ?? EnsureGuestSession(sessionId);
        session.Email = email;
        session.FirstName = firstName;
        session.LastName = lastName;
        session.Phone = phone;
        Save(sessionId, session);
    }

    public void UpdateDeliveryAddress(string sessionId, PosSessionAddress address)
    {
        var session = GetSessionRaw(sessionId) ?? EnsureGuestSession(sessionId);
        session.DeliveryAddress = address;
        if (session is RegisteredUserPosSession registered)
        {
            registered.SelectedAddressId = null; // free-form override
        }
        Save(sessionId, session);
    }

    public void ClearSession(string sessionId) => _cache.Remove(CacheKey(sessionId));

    private async Task<RegisteredUserPosSession> PromoteGuestToUserAsync(
        string sessionId, GuestPosSession guest, string applicationUserId)
    {
        var user = await _userManager.FindByIdAsync(applicationUserId)
            ?? throw new InvalidOperationException($"User {applicationUserId} not found.");

        var promoted = new RegisteredUserPosSession
        {
            ApplicationUserId = user.Id,
            Email = user.Email ?? guest.Email,
            FirstName = !string.IsNullOrWhiteSpace(user.FirstName) ? user.FirstName : guest.FirstName,
            LastName = !string.IsNullOrWhiteSpace(user.LastName) ? user.LastName : guest.LastName,
            Phone = !string.IsNullOrWhiteSpace(user.PhoneNumber) ? user.PhoneNumber : guest.Phone,
        };

        var defaultAddress = (await _addressService.GetForUserAsync(user.Id)).FirstOrDefault();
        if (defaultAddress != null)
        {
            promoted.SelectedAddressId = defaultAddress.Id;
            promoted.DeliveryAddress = ToSessionAddress(defaultAddress);
        }
        else if (guest.DeliveryAddress != null && !guest.DeliveryAddress.IsEmpty)
        {
            promoted.DeliveryAddress = guest.DeliveryAddress;
        }

        Save(sessionId, promoted);
        return promoted;
    }

    private void Save(string sessionId, PosCustomerSession session)
    {
        _cache.Set(CacheKey(sessionId), session, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SessionExpiration
        });
    }

    private static PosSessionAddress ToSessionAddress(Address address) => new()
    {
        Label = address.Label,
        Street = address.Street,
        HouseNumber = address.HouseNumber,
        PostalCode = address.PostalCode,
        City = address.City,
        State = address.State,
        Country = address.Country,
    };

    private static string CacheKey(string sessionId) => $"pos_customer_{sessionId}";
}
