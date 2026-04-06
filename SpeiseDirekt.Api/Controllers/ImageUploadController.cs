using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers
{
    [Route("api/images")]
    [ApiController]
    [Authorize]
    public class ImageUploadController : ControllerBase
    {
        private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

        private readonly IImageRepository _imageRepository;
        private readonly IImageResizeService _imageResizeService;

        public ImageUploadController(IImageRepository imageRepository, IImageResizeService imageResizeService)
        {
            _imageRepository = imageRepository;
            _imageResizeService = imageResizeService;
        }

        [HttpPost]
        public async Task<ActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file provided.");

            if (file.Length > MaxFileSize)
                return BadRequest("File size exceeds the 5MB limit.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                return BadRequest($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}");

            using var inputStream = file.OpenReadStream();
            using var outputStream = new MemoryStream();
            await _imageResizeService.ResizeImageAsync(inputStream, outputStream, 600);

            var image = new Image
            {
                Id = Guid.NewGuid(),
                Content = outputStream.ToArray(),
                MimeType = file.ContentType
            };

            await _imageRepository.CreateAsync(image);

            return CreatedAtAction(nameof(GetImage), new { id = image.Id }, new
            {
                id = image.Id,
                url = $"/api/images/{image.Id}"
            });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(Guid id)
        {
            var deleted = await _imageRepository.DeleteAsync(id);
            if (!deleted)
                return NotFound();

            return NoContent();
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        [ResponseCache(Duration = 3600)]
        public async Task<ActionResult> GetImage(Guid id)
        {
            var image = await _imageRepository.GetByIdAsync(id);
            if (image == null)
                return NotFound();

            return File(image.Content, image.MimeType);
        }
    }
}
