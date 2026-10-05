using System;
using System.Drawing;
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
            // Try English first for MTGO card titles
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
        catch (Exception)
        {
            _isInitialized = false;
            return false;
        }
    }

    /// <summary>
    /// Recognizes text from a GDI Bitmap using hardware-accelerated Windows.Media.Ocr.
    /// </summary>
    public async Task<string> RecognizeTextAsync(Bitmap bitmap)
    {
        if (_ocrEngine == null || bitmap == null) return string.Empty;

        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Bmp);
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
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
