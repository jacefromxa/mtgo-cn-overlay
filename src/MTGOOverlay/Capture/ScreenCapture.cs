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
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Captures an area relative to the cursor position (e.g. for card hover recognition).
    /// </summary>
    public static Bitmap? CaptureNearCursor(Point cursorPos, int width = 320, int height = 60, int offsetY = -30)
    {
        var rect = new Rectangle(cursorPos.X - 10, cursorPos.Y + offsetY, width, height);
        return CaptureRegion(rect);
    }
}
