using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SupportAgent.Services;

/// <summary>
/// Vision Helper for Coarse-to-Fine image processing.
/// Enables token-efficient vision by sending low-res overviews first,
/// then high-res crops on demand.
/// </summary>
public static class VisionHelper
{
    private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "SupportAgent_Vision");

    static VisionHelper()
    {
        // Ensure temp folder exists
        Directory.CreateDirectory(TempFolder);
    }

    /// <summary>
    /// Creates a temporary file path in the vision temp folder
    /// </summary>
    public static string GetTempPath(string filename)
    {
        return Path.Combine(TempFolder, filename);
    }

    /// <summary>
    /// Cleans up old temporary vision files
    /// </summary>
    public static void CleanupOldFiles(int olderThanMinutes = 30)
    {
        try
        {
            if (!Directory.Exists(TempFolder)) return;

            var threshold = DateTime.Now.AddMinutes(-olderThanMinutes);
            var files = Directory.GetFiles(TempFolder);

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch { /* Ignore errors for individual files */ }
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    /// <summary>
    /// Creates a low-resolution overview of an image for token-efficient vision.
    /// Returns the scale factor used for resizing.
    /// </summary>
    /// <param name="originalPath">Path to the original high-res image</param>
    /// <param name="outputPath">Path where the low-res image will be saved</param>
    /// <param name="targetLongSide">Target size for the longest side (default: 1280px)</param>
    /// <returns>Scale factor (e.g., 0.5 means image was scaled down by 50%)</returns>
    public static double CreateLowResOverview(string originalPath, string outputPath, int targetLongSide = 1280)
    {
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original image not found", originalPath);
        }

        using (var original = Image.FromFile(originalPath))
        {
            // Calculate scale factor
            double scaleFactor = 1.0;
            int maxSide = Math.Max(original.Width, original.Height);

            if (maxSide > targetLongSide)
            {
                scaleFactor = (double)targetLongSide / maxSide;
            }
            else
            {
                // Image is already small enough, just copy it
                File.Copy(originalPath, outputPath, overwrite: true);
                Console.WriteLine($"  [Vision] Image already small ({original.Width}x{original.Height}), no resize needed");
                return 1.0;
            }

            int newW = (int)(original.Width * scaleFactor);
            int newH = (int)(original.Height * scaleFactor);

            // Create high-quality resized image
            using (var resized = new Bitmap(newW, newH))
            using (var g = Graphics.FromImage(resized))
            {
                // High-quality settings for text readability
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                g.DrawImage(original, 0, 0, newW, newH);

                // Save with JPEG quality 85 (balance between size and clarity)
                SaveJpeg(resized, outputPath, 85L);
            }

            Console.WriteLine($"  [Vision] Resized {original.Width}x{original.Height} → {newW}x{newH} (scale: {scaleFactor:F4})");
            return scaleFactor;
        }
    }

    /// <summary>
    /// Creates a high-resolution crop from the original image based on coordinates
    /// provided by the LLM (which were calculated for the low-res version).
    /// </summary>
    /// <param name="originalPath">Path to the original high-res image</param>
    /// <param name="outputPath">Path where the crop will be saved</param>
    /// <param name="llmX">X coordinate from LLM (based on low-res image)</param>
    /// <param name="llmY">Y coordinate from LLM (based on low-res image)</param>
    /// <param name="llmW">Width from LLM (based on low-res image)</param>
    /// <param name="llmH">Height from LLM (based on low-res image)</param>
    /// <param name="scaleFactor">Scale factor used when creating the low-res version</param>
    public static void CreateHighResCrop(
        string originalPath,
        string outputPath,
        int llmX,
        int llmY,
        int llmW,
        int llmH,
        double scaleFactor)
    {
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original image not found", originalPath);
        }

        using (var original = Image.FromFile(originalPath))
        {
            // Convert low-res coordinates back to high-res coordinates
            int realX = (int)(llmX / scaleFactor);
            int realY = (int)(llmY / scaleFactor);
            int realW = (int)(llmW / scaleFactor);
            int realH = (int)(llmH / scaleFactor);

            // Clamp to image boundaries
            if (realX < 0) realX = 0;
            if (realY < 0) realY = 0;
            if (realW <= 0) realW = 200; // Minimum crop size
            if (realH <= 0) realH = 200;

            if (realX + realW > original.Width) realW = original.Width - realX;
            if (realY + realH > original.Height) realH = original.Height - realY;

            // Ensure we still have a valid crop area
            if (realW <= 0 || realH <= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid crop coordinates after scaling: X={realX}, Y={realY}, W={realW}, H={realH}");
            }

            Console.WriteLine($"  [Vision] Cropping: LLM coords [{llmX},{llmY},{llmW}x{llmH}] → Real coords [{realX},{realY},{realW}x{realH}]");

            // Create the crop
            Rectangle cropRect = new Rectangle(realX, realY, realW, realH);
            using (var crop = new Bitmap(realW, realH))
            using (var g = Graphics.FromImage(crop))
            {
                g.DrawImage(
                    original,
                    new Rectangle(0, 0, realW, realH),
                    cropRect,
                    GraphicsUnit.Pixel);

                // Save with high quality (95) for detailed inspection
                SaveJpeg(crop, outputPath, 95L);
            }

            Console.WriteLine($"  [Vision] High-res crop saved: {realW}x{realH} at quality 95%");
        }
    }

    /// <summary>
    /// Helper to save JPEG with specific quality setting
    /// ASYNC: Uses fire-and-forget pattern to avoid blocking main thread on disk I/O
    /// </summary>
    private static void SaveJpeg(Image img, string path, long quality)
    {
        // CRITICAL: Clone image before passing to background thread
        // Original will be disposed by caller's using block
        Image imgCopy = (Image)img.Clone();

        // Fire-and-forget async save (non-blocking)
        Task.Run(() =>
        {
            try
            {
                var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                if (jpegEncoder == null)
                {
                    // Fallback: save without quality parameter
                    imgCopy.Save(path, ImageFormat.Jpeg);
                }
                else
                {
                    var encoderParameters = new EncoderParameters(1);
                    encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    imgCopy.Save(path, jpegEncoder, encoderParameters);
                }
            }
            catch (Exception ex)
            {
                // Suppress I/O errors to avoid crashing agent on disk issues
                Console.WriteLine($"  ⚠️ [Async] JPEG save failed ({path}): {ex.Message}");
            }
            finally
            {
                // Always dispose the clone in background thread
                imgCopy.Dispose();
            }
        });
    }

    /// <summary>
    /// Gets the JPEG encoder for quality control
    /// </summary>
    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    /// <summary>
    /// Converts an image file to Base64 string for transmission
    /// </summary>
    public static string ImageToBase64(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            return "";
        }

        try
        {
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            return Convert.ToBase64String(imageBytes);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Saves a Base64 image string to a file
    /// </summary>
    public static void Base64ToImageFile(string base64String, string outputPath)
    {
        if (string.IsNullOrEmpty(base64String))
        {
            throw new ArgumentException("Base64 string is empty", nameof(base64String));
        }

        byte[] imageBytes = Convert.FromBase64String(base64String);
        File.WriteAllBytes(outputPath, imageBytes);
    }
}
