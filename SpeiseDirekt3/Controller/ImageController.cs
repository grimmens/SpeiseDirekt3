using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Data;
using QRCoder;
using SpeiseDirekt3.Components.QrCodes;

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

        /// <summary>
        /// QR code PNG that encodes the guest link <c>/qr/{qrId}</c>. With <c>?download=1</c> it is sent as an
        /// attachment named <c>name</c> (see <see cref="QrCodeLinks.DownloadFileName"/>), e.g. <c>qr-tisch-1.png</c>.
        /// </summary>
        [HttpGet("qr/{qrId}")]
        [ResponseCache(Duration = 3600)]
        public IActionResult GetQrCode([FromRoute] Guid qrId, [FromQuery] string? download = null, [FromQuery] string? name = null)
        {
            var targetUrl = $"{Request.Scheme}://{Request.Host}/qr/{qrId}";

            using var qrGen = new QRCodeGenerator();
            using var qrData = qrGen.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(qrData);
            var bytes = png.GetGraphic(20);

            // "1" as in the links on the QR pages (bool binding would reject it)
            return download is "1" or "true"
                ? File(bytes, "image/png", QrCodeLinks.SanitizeDownloadName(name) ?? QrCodeLinks.DownloadFileName(null))
                : File(bytes, "image/png");
        }
    }
}
