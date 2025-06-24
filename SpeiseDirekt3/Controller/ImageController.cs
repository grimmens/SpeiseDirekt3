using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt3.Data;

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
    }
}
