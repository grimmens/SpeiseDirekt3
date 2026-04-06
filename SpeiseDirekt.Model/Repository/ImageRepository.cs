using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class ImageRepository : IImageRepository
{
    private readonly ApplicationDbContext _db;

    public ImageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Image> CreateAsync(Image image)
    {
        _db.Images.Add(image);
        await _db.SaveChangesAsync();
        return image;
    }

    public async Task<Image?> GetByIdAsync(Guid id)
    {
        return await _db.Images.FindAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var image = await _db.Images.FindAsync(id);
        if (image is null)
            return false;

        _db.Images.Remove(image);
        await _db.SaveChangesAsync();
        return true;
    }
}
