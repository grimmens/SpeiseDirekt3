namespace SpeiseDirekt.ServiceInterface;

public class PosCustomerSession
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsGuest { get; set; } = true;
}

public interface IPosCustomerService
{
    PosCustomerSession? GetSession(string sessionId);
    void SaveSession(string sessionId, PosCustomerSession session);
    void ClearSession(string sessionId);
}
