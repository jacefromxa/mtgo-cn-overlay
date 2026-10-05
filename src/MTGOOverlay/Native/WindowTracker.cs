using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MTGOOverlay.Native;

public class WindowTracker
{
    private IntPtr _mtgoWindowHandle = IntPtr.Zero;
    private DateTime _lastSearchTime = DateTime.MinValue;

    /// <summary>
    /// Gets the current MTGO main window handle if running and visible.
    /// </summary>
    public IntPtr GetMtgoWindowHandle()
    {
        // Cache handle for 1.5 seconds to avoid heavy process enumeration
        if (_mtgoWindowHandle != IntPtr.Zero && (DateTime.UtcNow - _lastSearchTime).TotalSeconds < 1.5)
        {
            if (IsWindowValid(_mtgoWindowHandle))
            {
                return _mtgoWindowHandle;
            }
        }

        _mtgoWindowHandle = FindMtgoWindow();
        _lastSearchTime = DateTime.UtcNow;
        return _mtgoWindowHandle;
    }

    /// <summary>
    /// Checks if the cursor is currently inside the MTGO window bounds.
    /// </summary>
    public bool IsCursorInsideMtgo(out Point cursorPos, out Rectangle windowRect)
    {
        cursorPos = Point.Empty;
        windowRect = Rectangle.Empty;

        if (!Win32.GetCursorPos(out var pt)) return false;
        cursorPos = new Point(pt.X, pt.Y);

        IntPtr hWnd = GetMtgoWindowHandle();
        if (hWnd == IntPtr.Zero) return false;

        if (!Win32.GetWindowRect(hWnd, out var rect)) return false;
        windowRect = rect.ToRectangle();

        return windowRect.Contains(cursorPos);
    }

    private static bool IsWindowValid(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        if (!Win32.GetWindowRect(hWnd, out var rect)) return false;
        return rect.Width > 100 && rect.Height > 100;
    }

    private static IntPtr FindMtgoWindow()
    {
        // 1. Search by process name
        var processes = Process.GetProcessesByName("MTGO");
        foreach (var proc in processes)
        {
            if (proc.MainWindowHandle != IntPtr.Zero && IsWindowValid(proc.MainWindowHandle))
            {
                return proc.MainWindowHandle;
            }
        }

        // 2. Fallback search by window title containing "Magic: The Gathering Online"
        var foreground = Win32.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var sb = new StringBuilder(256);
            Win32.GetWindowText(foreground, sb, sb.Capacity);
            string title = sb.ToString();
            if (title.Contains("Magic: The Gathering Online", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("MTGO", StringComparison.OrdinalIgnoreCase))
            {
                return foreground;
            }
        }

        return IntPtr.Zero;
    }
}
