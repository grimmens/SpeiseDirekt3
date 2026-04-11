using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class AddressService : IAddressService
{
    private readonly ApplicationDbContext _db;

    public AddressService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<List<Address>> GetForUserAsync(string userId) =>
        _db.Addresses
            .AsNoTracking()
            .Where(a => a.ApplicationUserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();

    public Task<Address?> GetByIdAsync(Guid id, string userId) =>
        _db.Addresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.ApplicationUserId == userId);

    public async Task<Address> CreateAsync(string userId, Address address)
    {
        address.Id = address.Id == Guid.Empty ? Guid.NewGuid() : address.Id;
        address.ApplicationUserId = userId;
        address.CreatedAt = DateTime.UtcNow;

        if (address.IsDefault)
            await ClearDefaultsAsync(userId);

        _db.Addresses.Add(address);
        await _db.SaveChangesAsync();
        return address;
    }

    public async Task<Address?> UpdateAsync(Guid id, string userId, Action<Address> apply)
    {
        var existing = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == id && a.ApplicationUserId == userId);
        if (existing is null) return null;

        apply(existing);

        if (existing.IsDefault)
            await ClearDefaultsAsync(userId, exceptId: id);

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, string userId)
    {
        var existing = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == id && a.ApplicationUserId == userId);
        if (existing is null) return false;

        _db.Addresses.Remove(existing);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetDefaultAsync(Guid id, string userId)
    {
        var target = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == id && a.ApplicationUserId == userId);
        if (target is null) return false;

        await ClearDefaultsAsync(userId, exceptId: id);
        target.IsDefault = true;
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task ClearDefaultsAsync(string userId, Guid? exceptId = null)
    {
        var defaults = await _db.Addresses
            .Where(a => a.ApplicationUserId == userId && a.IsDefault && (exceptId == null || a.Id != exceptId))
            .ToListAsync();
        foreach (var a in defaults)
            a.IsDefault = false;
    }
}
