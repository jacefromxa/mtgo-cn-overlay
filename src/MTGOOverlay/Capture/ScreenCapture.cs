using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace MTGOOverlay.Capture;

public static class ScreenCapture
{
    /// <summary>
    /// Captures a specified screen rectangle into a Bitmap with screen bounds safety clamping.
    /// </summary>
    public static Bitmap? CaptureRegion(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0) return null;

        try
        {
            int left = Math.Max(0, region.Left);
            int top = Math.Max(0, region.Top);
            int width = Math.Max(10, region.Width);
            int height = Math.Max(10, region.Height);

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
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
    /// Captures the full card zone around the cursor.
    /// In MTGO, a battlefield card is typically 120-240px tall. By capturing 160px above and 60px below,
    /// the top title bar of the card is guaranteed to be captured regardless of whether the user
    /// hovers over the title, art, or rules text box.
    /// </summary>
    public static Bitmap? CaptureNearCursor(Point cursorPos, int width = 360, int height = 220, int offsetY = -160)
    {
        var rect = new Rectangle(cursorPos.X - 50, cursorPos.Y + offsetY, width, height);
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
