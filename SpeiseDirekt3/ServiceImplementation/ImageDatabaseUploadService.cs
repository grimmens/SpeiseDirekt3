using Microsoft.AspNetCore.Components.Forms;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class ImageDatabaseUploadService : IImageUploadService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageUploadService> _logger;
        private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        private readonly ApplicationDbContext context;
        private readonly IImageResizeService imageResizeService;
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

        public ImageDatabaseUploadService(ApplicationDbContext context, IImageResizeService imageResizeService)
        {
            this.context = context;
            this.imageResizeService = imageResizeService;
        }
        public async Task<bool> DeleteImageAsync(string imagePath)
        {
            if (Guid.TryParse(imagePath, out var guid))
            {
                var image = context.Images.Find(guid);
                if (image != null)
                {
                    context.Images.Remove(image);
                    await context.SaveChangesAsync();
                }
            }
            return true;
        }

        public string GetImageUrl(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return "/images/no-image-placeholder.svg"; // Default placeholder

            return $"/{imagePath}";
        }

        public bool IsValidImageFile(IBrowserFile file)
        {
            if (file == null)
                return false;

            // Check file size
            if (file.Size > MaxFileSize)
                return false;

            // Check file extension
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
                return false;

            // Check content type
            var allowedContentTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
            if (!allowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                return false;

            return true;
        }

        public async Task<string?> UploadImageAsync(IBrowserFile file)
        {
            try
            {
                if (!IsValidImageFile(file))
                {
                    _logger.LogWarning("Invalid image file: {FileName}", file.Name);
                    return null;
                }

                var id = Guid.NewGuid();

                // Generate unique filename
                var extension = Path.GetExtension(file.Name).ToLowerInvariant();
                using var memStream = new MemoryStream();
                using var memStream2 = new MemoryStream();
                await file.OpenReadStream(MaxFileSize).CopyToAsync(memStream);
                memStream.Position = 0;

                await imageResizeService.ResizeImageAsync(memStream, memStream2);
                memStream2.Position = 0;
                var model = new Image()
                {
                    MimeType = file.ContentType,
                    Content = memStream2.ToArray()
                };
                await context.AddAsync(model);
                await context.SaveChangesAsync();

                // Return relative path for database storage
                return $"image/{model.Id}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image: {FileName}", file.Name);
                return null;
            }
        }
    }
}
