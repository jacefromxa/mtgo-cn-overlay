using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MTGOOverlay.Ocr;

public class WindowsOcrEngine
{
    private OcrEngine? _ocrEngine;
    private bool _isInitialized;

    public bool IsAvailable => _isInitialized && _ocrEngine != null;

    public bool Initialize()
    {
        try
        {
            // MTGO card titles are English; try en-US first
            var lang = new Language("en-US");
            if (OcrEngine.IsLanguageSupported(lang))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
            }
            else
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            _isInitialized = _ocrEngine != null;
            return _isInitialized;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize OCR: {ex.Message}");
            _isInitialized = false;
            return false;
        }
    }

    /// <summary>
    /// Preprocesses image by upscaling small crops (2x) and enhancing contrast for high OCR accuracy.
    /// </summary>
    private static Bitmap Preprocess(Bitmap source)
    {
        // Dynamic scaling: Windows OCR is optimized for text line height around 80-120px.
        // If the user shrinks the preview window down to save screen space (e.g. title height is only 20-35px),
        // we scale up to 3x or 4x with bicubic interpolation so stroke details remain crisp and legible!
        int scale = 1;
        if (source.Height < 35)
        {
            scale = 4;
        }
        else if (source.Height < 55)
        {
            scale = 3;
        }
        else if (source.Height < 80)
        {
            scale = 2;
        }

        if (scale == 1) return (Bitmap)source.Clone();

        int targetWidth = source.Width * scale;
        int targetHeight = source.Height * scale;

        var enhanced = new Bitmap(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(enhanced))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;

            // Draw with slight contrast boost
            using var attributes = new ImageAttributes();
            float contrast = 1.2f;
            float t = (1.0f - contrast) / 2.0f;
            ColorMatrix cm = new ColorMatrix(new float[][]
            {
                new float[] { contrast, 0, 0, 0, 0 },
                new float[] { 0, contrast, 0, 0, 0 },
                new float[] { 0, 0, contrast, 0, 0 },
                new float[] { 0, 0, 0, 1.0f, 0 },
                new float[] { t, t, t, 0, 1.0f }
            });
            attributes.SetColorMatrix(cm);

            g.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        return enhanced;
    }

    /// <summary>
    /// Recognizes text from a GDI Bitmap using hardware-accelerated Windows.Media.Ocr.
    /// </summary>
    public async Task<string> RecognizeTextAsync(Bitmap bitmap)
    {
        if (_ocrEngine == null || bitmap == null) return string.Empty;

        try
        {
            using var processedBmp = Preprocess(bitmap);
            using var ms = new MemoryStream();
            processedBmp.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;

            using var randomAccessStream = ms.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
            if (ocrResult == null || ocrResult.Lines.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var line in ocrResult.Lines)
            {
                sb.AppendLine(line.Text);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR recognition error: {ex.Message}");
            return string.Empty;
        }
    }
}
