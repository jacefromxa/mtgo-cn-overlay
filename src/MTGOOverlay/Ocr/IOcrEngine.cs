using System;
using System.Drawing;
using System.Threading.Tasks;
using MTGOOverlay.Models;

namespace MTGOOverlay.Ocr;

/// <summary>
/// Unified OCR engine interface supporting multiple backends (PaddleOCR Deep Learning, Windows.Media.Ocr).
/// </summary>
public interface IOcrEngine : IDisposable
{
    string EngineName { get; }
    bool IsAvailable { get; }
    bool Initialize();
    Task<(string rawOcr, CardEntry? card)> RecognizeAndLookupAsync(
        Bitmap bitmap,
        Func<string, CardEntry?> lookup);
    Task<string> RecognizeTextAsync(Bitmap bitmap);
}

