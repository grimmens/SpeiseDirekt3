using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt3.Data;
using QRCoder;

namespace SpeiseDirekt3.Controller
{
    [ApiController]
    [Route("[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly ApplicationDbContext context;

        public ImageController(ApplicationDbContext context)
        {
            this.context = context;
        }
        [HttpGet("{id}")]
        [ResponseCache(Duration = 3600)]
        public async Task<IActionResult> GetImage([FromRoute] string id)
        {
            if (Guid.TryParse(id, out var imageId))
            {
                var entity = context.Images.Find(imageId);
                if (entity == null)
                {
                    return NotFound();
                }
                return File(entity.Content, entity.MimeType);
            }
            return NotFound();
        }

        [HttpGet("qr/{qrId}")]
        [ResponseCache(Duration = 3600)]
        public IActionResult GetQrCode([FromRoute] Guid qrId)
        {
            var targetUrl = $"{Request.Scheme}://{Request.Host}/qr/{qrId}";

            using var qrGen = new QRCodeGenerator();
            using var qrData = qrGen.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(qrData);
            var bytes = png.GetGraphic(20);

            return File(bytes, "image/png");
        }
    }
}
