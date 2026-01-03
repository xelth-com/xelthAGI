using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SupportAgent.Services;

/// <summary>
/// OCR Service using Windows native OCR engine (Windows 10+).
/// Converts GDI+ Bitmaps to WinRT SoftwareBitmaps for recognition.
/// </summary>
public class OcrService
{
    private OcrEngine? _ocrEngine;
    private bool _isInitialized;

    /// <summary>
    /// Gets whether OCR is supported on this system.
    /// Requires Windows 10 version 1904 (May 2020 Update) or later.
    /// </summary>
    public bool IsSupported => OcrEngine.IsAvailableProfileSupported;

    public OcrService()
    {
        try
        {
            if (IsSupported)
            {
                // Try to create OCR engine using user's preferred languages
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                _isInitialized = _ocrEngine != null;

                if (_isInitialized)
                {
                    Console.WriteLine("  ✅ OCR Engine initialized successfully");
                }
                else
                {
                    Console.WriteLine("  ⚠️ OCR Engine creation returned null");
                }
            }
            else
            {
                Console.WriteLine("  ⚠️ OCR not supported on this Windows version");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️ OCR Init Warning: {ex.Message}");
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Extracts text from a screen bitmap using Windows OCR.
    /// Returns formatted text with word locations for clickable coordinates.
    /// </summary>
    /// <param name="bitmap">GDI+ Bitmap to analyze</param>
    /// <returns>OCR result text or error message</returns>
    public async Task<string> GetTextFromScreen(Bitmap bitmap)
    {
        if (!_isInitialized || _ocrEngine == null)
        {
            return "OCR_UNAVAILABLE (Feature not supported on this OS)";
        }

        try
        {
            // Convert System.Drawing.Bitmap (GDI+) to Windows.Graphics.Imaging.SoftwareBitmap (WinRT)
            using var stream = new InMemoryRandomAccessStream();

            // Save to memory stream as PNG (lossless, preserves text clarity)
            bitmap.Save(stream.AsStream(), ImageFormat.Png);
            stream.Seek(0);

            // Decode the image
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            // Run OCR
            var result = await _ocrEngine.RecognizeAsync(softwareBitmap);

            if (result.Lines.Count == 0)
            {
                return "OCR_RESULT: [No Text Found]";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"OCR_RESULT ({result.Lines.Count} lines found):");

            // Format: "Word" @(CenterX,CenterY)
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;

                // Calculate bounding box of the line
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    if (r.X < minX) minX = r.X;
                    if (r.Y < minY) minY = r.Y;
                    if (r.X + r.Width > maxX) maxX = r.X + r.Width;
                    if (r.Y + r.Height > maxY) maxY = r.Y + r.Height;
                }

                int cx = (int)(minX + (maxX - minX) / 2);
                int cy = (int)(minY + (maxY - minY) / 2);

                // Clean text for display
                string cleanText = line.Text.Replace("\n", " ").Replace("\r", " ").Trim();

                if (!string.IsNullOrWhiteSpace(cleanText))
                {
                    sb.AppendLine($" - \"{cleanText}\" @({cx},{cy})");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"OCR_ERROR: {ex.Message}";
        }
    }
}
