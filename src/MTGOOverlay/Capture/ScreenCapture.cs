using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace MTGOOverlay.Capture;

public static class ScreenCapture
{
    /// <summary>
    /// Captures a specified screen rectangle into a Bitmap.
    /// </summary>
    public static Bitmap? CaptureRegion(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0) return null;

        try
        {
            var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CaptureRegion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Captures an area relative to the cursor position (e.g. for card hover recognition).
    /// </summary>
    public static Bitmap? CaptureNearCursor(Point cursorPos, int width = 340, int height = 70, int offsetY = -35)
    {
        var rect = new Rectangle(cursorPos.X - 10, cursorPos.Y + offsetY, width, height);
        return CaptureRegion(rect);
    }

    /// <summary>
    /// Captures the title strip of MTGO's Card Preview pane.
    /// In MTGO's preview dock, the card title is located at ~3% to 12% from the top.
    /// </summary>
    public static Bitmap? CapturePreviewTitle(Rectangle previewPaneRect)
    {
        if (previewPaneRect.Width < 50 || previewPaneRect.Height < 50) return null;

        int titleTop = previewPaneRect.Top + (int)(previewPaneRect.Height * 0.03);
        int titleHeight = Math.Max(28, (int)(previewPaneRect.Height * 0.10));
        int titleLeft = previewPaneRect.Left + (int)(previewPaneRect.Width * 0.04);
        int titleWidth = Math.Max(50, (int)(previewPaneRect.Width * 0.92));

        var titleRect = new Rectangle(titleLeft, titleTop, titleWidth, titleHeight);
        return CaptureRegion(titleRect);
    }

    /// <summary>
    /// Computes a fast 64-bit sampling hash of an image by checking 16 grid sample points.
    /// Allows checking if the preview pane has changed without running OCR.
    /// </summary>
    public static ulong ComputeFastSampleHash(Bitmap bmp)
    {
        if (bmp.Width < 8 || bmp.Height < 8) return 0;

        ulong hash = 14695981039346656037UL; // FNV-1a offset
        int stepX = Math.Max(1, bmp.Width / 5);
        int stepY = Math.Max(1, bmp.Height / 5);

        for (int y = stepY; y < bmp.Height - 1; y += stepY)
        {
            for (int x = stepX; x < bmp.Width - 1; x += stepX)
            {
                var color = bmp.GetPixel(x, y);
                int argb = color.ToArgb();
                hash ^= (ulong)argb;
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }
}
