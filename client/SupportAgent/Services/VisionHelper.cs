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
    /// Creates a low-resolution SQUARE overview of an image for token-efficient vision.
    /// Adds padding to make the image square while preserving original content at 0,0.
    /// Returns the scale factor used for resizing.
    /// </summary>
    /// <param name="originalPath">Path to the original high-res image</param>
    /// <param name="outputPath">Path where the low-res image will be saved</param>
    /// <param name="targetLongSide">Target size for the square side (default: 1280px)</param>
    /// <returns>Scale factor (e.g., 0.5 means image was scaled down by 50%)</returns>
    public static double CreateLowResOverview(string originalPath, string outputPath, int targetLongSide = 1280)
    {
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original image not found", originalPath);
        }

        using (var original = Image.FromFile(originalPath))
        {
            // Step 1: Determine the square size (max of width/height)
            int squareSize = Math.Max(original.Width, original.Height);

            // Step 2: Calculate scale factor to fit into targetLongSide
            double scaleFactor = 1.0;
            if (squareSize > targetLongSide)
            {
                scaleFactor = (double)targetLongSide / squareSize;
            }

            // Scaled dimensions of original content
            int scaledW = (int)(original.Width * scaleFactor);
            int scaledH = (int)(original.Height * scaleFactor);

            // Final square size
            int finalSize = (int)(squareSize * scaleFactor);

            // Create square image with padding
            using (var squareImage = new Bitmap(finalSize, finalSize))
            using (var g = Graphics.FromImage(squareImage))
            {
                // Fill with dark gray (not pure black, to distinguish from content)
                g.Clear(Color.FromArgb(32, 32, 32));

                // High-quality settings for text readability
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Draw original content at 0,0 (top-left, no offset!)
                // This preserves coordinate system - 0,0 is always top-left of real content
                g.DrawImage(original, 0, 0, scaledW, scaledH);

                // Save with JPEG quality 85
                SaveJpeg(squareImage, outputPath, 85L);
            }

            // Log the transformation
            string paddingInfo = original.Width > original.Height
                ? $"bottom padding {finalSize - scaledH}px"
                : $"right padding {finalSize - scaledW}px";

            Console.WriteLine($"  [Vision] {original.Width}x{original.Height} → {finalSize}x{finalSize} square ({paddingInfo}, scale: {scaleFactor:F4})");

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
    /// SYNCHRONOUS: Blocks until file is written to ensure it exists before caller continues
    /// </summary>
    private static void SaveJpeg(Image img, string path, long quality)
    {
        try
        {
            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            if (jpegEncoder == null)
            {
                // Fallback: save without quality parameter
                img.Save(path, ImageFormat.Jpeg);
            }
            else
            {
                var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                img.Save(path, jpegEncoder, encoderParameters);
            }
        }
        catch (Exception ex)
        {
            // Log error but allow caller to handle
            Console.WriteLine($"  ⚠️ JPEG save failed ({path}): {ex.Message}");
            throw; // Rethrow so caller knows save failed
        }
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
