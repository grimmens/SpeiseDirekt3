using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IImageRepository
{
    Task<Image> CreateAsync(Image image);
    Task<Image?> GetByIdAsync(Guid id);
    Task<bool> DeleteAsync(Guid id);
}
