using Microsoft.AspNetCore.Components.Forms;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class ImageUploadService : IImageUploadService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageUploadService> _logger;
        private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

        public ImageUploadService(IWebHostEnvironment environment, ILogger<ImageUploadService> logger)
        {
            _environment = environment;
            _logger = logger;
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

                // Create upload directory if it doesn't exist
                var uploadPath = Path.Combine(_environment.WebRootPath, "uploads", "menu-items");
                Directory.CreateDirectory(uploadPath);

                // Generate unique filename
                var extension = Path.GetExtension(file.Name).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadPath, fileName);

                // Save file
                using var stream = new FileStream(filePath, FileMode.Create);
                await file.OpenReadStream(MaxFileSize).CopyToAsync(stream);

                // Return relative path for database storage
                return $"uploads/menu-items/{fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image: {FileName}", file.Name);
                return null;
            }
        }

        public async Task<bool> DeleteImageAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath))
                    return true;

                var fullPath = Path.Combine(_environment.WebRootPath, imagePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("Deleted image: {ImagePath}", imagePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image: {ImagePath}", imagePath);
                return false;
            }
        }

        public string GetImageUrl(string? imagePath)
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
    }
}
