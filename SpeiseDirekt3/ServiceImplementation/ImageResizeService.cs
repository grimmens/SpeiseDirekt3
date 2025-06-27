using Microsoft.AspNetCore.WebUtilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class ImageResizeService : IImageResizeService
    {
        private const int MaxIterations = 1;
        private const double TolerancePercentage = 0.05; // 5% tolerance

        public async Task ResizeImageAsync(Stream inputStream, Stream outputStream, int targetSizeKb = 600)
        {
            var targetSizeBytes = targetSizeKb * 1024;

            using var image = await Image.LoadAsync(inputStream);
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            // Start with an estimated size reduction
            var scaleFactor = EstimateInitialScaleFactor(image, targetSizeBytes);

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var newWidth = (int)(originalWidth * scaleFactor);
                var newHeight = (int)(originalHeight * scaleFactor);

                // Ensure minimum dimensions
                if (newWidth < 100 || newHeight < 100)
                {
                    newWidth = Math.Max(100, newWidth);
                    newHeight = Math.Max(100, newHeight);
                }

                using var resizedImage = image.Clone(ctx => ctx.Resize(newWidth, newHeight));

                // Test the file size with current dimensions
                using var testStream = new MemoryStream();
                await SaveImageWithOptimalQuality(resizedImage, testStream, targetSizeBytes);

                var currentSize = testStream.Length;
                var sizeDifference = Math.Abs(currentSize - targetSizeBytes) / (double)targetSizeBytes;

                // If we're within tolerance, save and return
                if (sizeDifference <= TolerancePercentage)
                {
                    testStream.Seek(0, SeekOrigin.Begin);
                    await testStream.CopyToAsync(outputStream);
                    return;
                }

                // Adjust scale factor for next iteration
                if (currentSize > targetSizeBytes)
                {
                    // Image too large, reduce scale
                    scaleFactor *= Math.Sqrt((double)targetSizeBytes / currentSize);
                }
                else
                {
                    // Image too small, increase scale
                    scaleFactor *= Math.Sqrt((double)targetSizeBytes / currentSize);
                }

                // Prevent infinite loops with extreme scale factors
                scaleFactor = Math.Max(0.1, Math.Min(2.0, scaleFactor));
            }

            // If we couldn't reach target size within iterations, save the last attempt
            var finalWidth = (int)(originalWidth * scaleFactor);
            var finalHeight = (int)(originalHeight * scaleFactor);

            using var finalImage = image.Clone(ctx => ctx.Resize(finalWidth, finalHeight));
            await SaveImageWithOptimalQuality(finalImage, outputStream, targetSizeBytes);
        }

        public async Task ResizeImageAsync(string inputPath, string outputPath, int targetSizeKb = 600)
        {
            using var inputStream = File.OpenRead(inputPath);
            using var outputStream = File.Create(outputPath);

            await ResizeImageAsync(inputStream, outputStream, targetSizeKb);
        }

        private static double EstimateInitialScaleFactor(Image image, long targetSizeBytes)
        {
            // Rough estimation based on pixel count
            // This is a heuristic - actual compression varies significantly
            var pixelCount = image.Width * image.Height;
            var estimatedBytesPerPixel = 3; // RGB estimate
            var estimatedCurrentSize = pixelCount * estimatedBytesPerPixel;

            var initialScale = Math.Sqrt((double)targetSizeBytes / estimatedCurrentSize);

            // Start conservatively to avoid overshooting
            return Math.Min(initialScale * 0.8, 0.95);
        }

        private static async Task SaveImageWithOptimalQuality(Image image, Stream outputStream, long targetSizeBytes)
        {
            // Try different formats and quality settings to find the best fit
            var bestStream = new MemoryStream();
            var bestSize = long.MaxValue;
            var bestSizeDifference = double.MaxValue;

            // Test JPEG with different quality levels
            var jpegResult = await TestJpegQuality(image, targetSizeBytes);
            if (jpegResult.sizeDifference < bestSizeDifference)
            {
                bestSizeDifference = jpegResult.sizeDifference;
                bestSize = jpegResult.size;
                bestStream?.Dispose();
                bestStream = jpegResult.stream;
            }
            else
            {
                jpegResult.stream?.Dispose();
            }

            // Test WebP if it produces better results
            var webpResult = await TestWebpQuality(image, targetSizeBytes);
            if (webpResult.sizeDifference < bestSizeDifference)
            {
                bestSizeDifference = webpResult.sizeDifference;
                bestSize = webpResult.size;
                bestStream?.Dispose();
                bestStream = webpResult.stream;
            }
            else
            {
                webpResult.stream?.Dispose();
            }

            // Test PNG (usually larger, but included for completeness)
            var pngResult = await TestPngCompression(image, targetSizeBytes);
            if (pngResult.sizeDifference < bestSizeDifference)
            {
                bestSizeDifference = pngResult.sizeDifference;
                bestSize = pngResult.size;
                bestStream?.Dispose();
                bestStream = pngResult.stream;
            }
            else
            {
                pngResult.stream?.Dispose();
            }

            bestStream.Seek(0, SeekOrigin.Begin);
            await bestStream.CopyToAsync(outputStream);
            bestStream.Dispose();
        }

        private static async Task<(MemoryStream stream, long size, double sizeDifference)> TestJpegQuality(Image image, long targetSizeBytes)
        {
            var qualityLevels = new[] { 85, 75, 65, 55, 45, 35, 95 };
            MemoryStream bestStream = null;
            var bestSize = long.MaxValue;
            var bestSizeDifference = double.MaxValue;

            foreach (var quality in qualityLevels)
            {
                using var testStream = new MemoryStream();
                var encoder = new JpegEncoder { Quality = quality };
                await image.SaveAsJpegAsync(testStream, encoder);

                var currentSize = testStream.Length;
                var sizeDifference = Math.Abs(currentSize - targetSizeBytes) / (double)targetSizeBytes;

                if (sizeDifference < bestSizeDifference)
                {
                    bestSizeDifference = sizeDifference;
                    bestSize = currentSize;
                    bestStream?.Dispose();
                    bestStream = new MemoryStream();
                    testStream.Seek(0, SeekOrigin.Begin);
                    await testStream.CopyToAsync(bestStream);
                }
            }

            return (bestStream, bestSize, bestSizeDifference);
        }

        private static async Task<(MemoryStream stream, long size, double sizeDifference)> TestWebpQuality(Image image, long targetSizeBytes)
        {
            var qualityLevels = new[] { 85, 75, 65, 55, 45, 35, 95 };
            MemoryStream bestStream = null;
            var bestSize = long.MaxValue;
            var bestSizeDifference = double.MaxValue;

            foreach (var quality in qualityLevels)
            {
                try
                {
                    using var testStream = new MemoryStream();
                    var encoder = new WebpEncoder { Quality = quality };
                    await image.SaveAsWebpAsync(testStream, encoder);

                    var currentSize = testStream.Length;
                    var sizeDifference = Math.Abs(currentSize - targetSizeBytes) / (double)targetSizeBytes;

                    if (sizeDifference < bestSizeDifference)
                    {
                        bestSizeDifference = sizeDifference;
                        bestSize = currentSize;
                        bestStream?.Dispose();
                        bestStream = new MemoryStream();
                        testStream.Seek(0, SeekOrigin.Begin);
                        await testStream.CopyToAsync(bestStream);
                    }
                }
                catch
                {
                    // WebP might not be available in all environments
                    continue;
                }
            }

            return (bestStream, bestSize, bestSizeDifference);
        }

        private static async Task<(MemoryStream stream, long size, double sizeDifference)> TestPngCompression(Image image, long targetSizeBytes)
        {
            MemoryStream bestStream = null;
            var bestSize = long.MaxValue;
            var bestSizeDifference = double.MaxValue;

            try
            {
                using var testStream = new MemoryStream();
                var encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                await image.SaveAsPngAsync(testStream, encoder);

                var currentSize = testStream.Length;
                var sizeDifference = Math.Abs(currentSize - targetSizeBytes) / (double)targetSizeBytes;

                bestSizeDifference = sizeDifference;
                bestSize = currentSize;
                bestStream = new MemoryStream();
                testStream.Seek(0, SeekOrigin.Begin);
                await testStream.CopyToAsync(bestStream);
            }
            catch
            {
                // PNG compression failed
            }

            return (bestStream, bestSize, bestSizeDifference);

        }
    }
}
