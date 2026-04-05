using Microsoft.AspNetCore.Components.Forms;

namespace SpeiseDirekt.ServiceInterface
{
    public interface IImageUploadService
    {
        Task<string?> UploadImageAsync(IBrowserFile file);
        Task<bool> DeleteImageAsync(string imagePath);
        string GetImageUrl(string imagePath);
        bool IsValidImageFile(IBrowserFile file);
    }
}
