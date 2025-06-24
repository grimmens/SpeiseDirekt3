namespace SpeiseDirekt3.ServiceInterface
{
    public interface IImageResizeService
    {
        /// <summary>
        /// Resizes an image to approximately 600KB while maintaining aspect ratio
        /// </summary>
        /// <param name="inputStream">Input image stream</param>
        /// <param name="outputStream">Output stream for resized image</param>
        /// <param name="targetSizeKb">Target file size in KB (default: 600)</param>
        /// <returns>Task representing the async operation</returns>
        Task ResizeImageAsync(Stream inputStream, Stream outputStream, int targetSizeKb = 600);

        /// <summary>
        /// Resizes an image file to approximately 600KB while maintaining aspect ratio
        /// </summary>
        /// <param name="inputPath">Path to input image file</param>
        /// <param name="outputPath">Path to output image file</param>
        /// <param name="targetSizeKb">Target file size in KB (default: 600)</param>
        /// <returns>Task representing the async operation</returns>
        Task ResizeImageAsync(string inputPath, string outputPath, int targetSizeKb = 600);
    }
}
