using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MTGOOverlay.Models;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;

namespace MTGOOverlay.Ocr;

/// <summary>
/// High-performance Deep Learning OCR engine based on PP-OCR v6 Tiny (via Sdcb.SimdPaddleOCR).
/// Pure C# managed implementation using AVX2 SIMD instructions.
/// Completely robust against dark card frames, textured backgrounds, and stylized fantasy serif fonts.
/// </summary>
public class PaddleOcrEngine : IOcrEngine
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PaddleOcrAll? _ocr;
    private bool _isInitialized;

    public string EngineName => "PaddleOCR (PP-OCRv6 SIMD)";
    public bool IsAvailable => _isInitialized && _ocr != null;

    public bool Initialize()
    {
        try
        {
            var options = new PaddleOcrOptions
            {
                UseDirectionClassification = false,
                Detector = new PaddleOcrDetectorOptions
                {
                    BoxThreshold = 0.3f,
                    BitmapThreshold = 0.15f
                }
            };

            _ocr = PaddleOcrAll.Load(ChineseV6TinyModels.Default, options);

            // Warmup inference with a 100x30 dummy bitmap to JIT compile SIMD kernels
            Warmup();

            _isInitialized = true;
            Debug.WriteLine("[PaddleOcrEngine] Initialized successfully with embedded PP-OCRv6 model.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PaddleOcrEngine] Failed to initialize: {ex.Message}");
            _isInitialized = false;
            return false;
        }
    }

    private void Warmup()
    {
        if (_ocr == null) return;
        try
        {
            using var dummy = new Bitmap(100, 30, PixelFormat.Format32bppArgb);
            var bmpData = dummy.LockBits(
                new Rectangle(0, 0, 100, 30),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            int len = Math.Abs(bmpData.Stride) * 30;
            byte[] bytes = new byte[len];
            dummy.UnlockBits(bmpData);

            _ocr.Run(bytes, 100, 30, bmpData.Stride, ImagePixelFormat.Bgra32);
        }
        catch
        {
            // Ignore warmup failures
        }
    }

    /// <summary>
    /// Upscales a small bitmap 2x with high-quality bicubic interpolation to optimize
    /// character stroke detection in DBNet / SVTR.
    /// </summary>
    private static Bitmap UpscaleForOcr(Bitmap src)
    {
        int scale = src.Height < 40 ? 3 : (src.Height < 70 ? 2 : 1);
        if (scale == 1) return (Bitmap)src.Clone();

        var dst = new Bitmap(src.Width * scale, src.Height * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(src, 0, 0, dst.Width, dst.Height);
        }
        return dst;
    }

    private string RunInferenceOnBitmap(Bitmap bmp)
    {
        if (_ocr == null) return string.Empty;

        using var prepared = UpscaleForOcr(bmp);
        var bmpData = prepared.LockBits(
            new Rectangle(0, 0, prepared.Width, prepared.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int totalBytes = Math.Abs(bmpData.Stride) * prepared.Height;
            byte[] bgraBytes = new byte[totalBytes];
            Marshal.Copy(bmpData.Scan0, bgraBytes, 0, totalBytes);

            var result = _ocr.Run(bgraBytes, prepared.Width, prepared.Height, bmpData.Stride, ImagePixelFormat.Bgra32);
            if (result.Lines != null && result.Lines.Length > 0)
            {
                // Sort lines strictly from top to bottom by their physical Y position on the card
                var sortedLines = result.Lines
                    .OrderBy(l => Math.Min(l.Box.Y1, Math.Min(l.Box.Y2, Math.Min(l.Box.Y3, l.Box.Y4))))
                    .Select(l => l.Text.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                return string.Join("\n", sortedLines);
            }
            return result.Text.Trim();
        }
        finally
        {
            prepared.UnlockBits(bmpData);
        }
    }

    public async Task<(string rawOcr, CardEntry? card)> RecognizeAndLookupAsync(
        Bitmap bitmap,
        Func<string, CardEntry?> lookup)
    {
        if (_ocr == null || bitmap == null || bitmap.Width < 10 || bitmap.Height < 10)
        {
            return (string.Empty, null);
        }

        await _lock.WaitAsync();
        try
        {
            // Pass 1: Run deep learning OCR on full bitmap with top-to-bottom spatial line ordering
            string text = RunInferenceOnBitmap(bitmap);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var card = lookup(text);
                if (card != null)
                {
                    string firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    return (firstLine, card);
                }
            }

            // Pass 2: If tall image didn't match directly, try upper 60% (focuses on card title & header zone)
            if (bitmap.Height > 80)
            {
                int upperH = (int)(bitmap.Height * 0.60);
                using var upperBmp = new Bitmap(bitmap.Width, upperH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(upperBmp))
                {
                    g.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, upperH),
                        0, 0, bitmap.Width, upperH, GraphicsUnit.Pixel);
                }

                string upperText = RunInferenceOnBitmap(upperBmp);
                if (!string.IsNullOrWhiteSpace(upperText))
                {
                    var cardUpper = lookup(upperText);
                    if (cardUpper != null)
                    {
                        string firstLine = upperText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        return (firstLine, cardUpper);
                    }
                    if (string.IsNullOrEmpty(text)) text = upperText;
                }
            }

            return (text, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PaddleOcrEngine] Inference error: {ex.Message}");
            return (string.Empty, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> RecognizeTextAsync(Bitmap bitmap)
    {
        var (ocr, _) = await RecognizeAndLookupAsync(bitmap, _ => null);
        return ocr;
    }

    public void Dispose()
    {
        _ocr?.Dispose();
        _ocr = null;
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}

