namespace SpeiseDirekt.ServiceInterface;

public sealed class PosSessionAddress
{
    public string? Label { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Street) &&
        string.IsNullOrWhiteSpace(City) &&
        string.IsNullOrWhiteSpace(PostalCode);
}

public abstract class PosCustomerSession
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public PosSessionAddress? DeliveryAddress { get; set; }

    public abstract bool IsGuest { get; }

    public string? FullName => string.Join(' ',
        new[] { FirstName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x))!);
}

public sealed class GuestPosSession : PosCustomerSession
{
    public override bool IsGuest => true;
}

public sealed class RegisteredUserPosSession : PosCustomerSession
{
    public override bool IsGuest => false;
    public string ApplicationUserId { get; set; } = default!;
    public Guid? SelectedAddressId { get; set; }
}

public interface IPosCustomerService
{
    /// <summary>
    /// Loads the session for the given id, promoting a guest session to a registered one
    /// when <paramref name="currentUserId"/> is set (and the cached session is a guest or
    /// belongs to a different user). Creates a new session if none exists.
    /// </summary>
    Task<PosCustomerSession> GetOrCreateSessionAsync(string sessionId, string? currentUserId);
    PosCustomerSession? GetSessionRaw(string sessionId);
    GuestPosSession EnsureGuestSession(string sessionId);
    Task<RegisteredUserPosSession> InitializeForUserAsync(string sessionId, string applicationUserId);
    Task<RegisteredUserPosSession?> SelectAddressAsync(string sessionId, Guid addressId);
    void UpdateContact(string sessionId, string? email, string? firstName, string? lastName, string? phone);
    void UpdateDeliveryAddress(string sessionId, PosSessionAddress address);
    void ClearSession(string sessionId);
}
