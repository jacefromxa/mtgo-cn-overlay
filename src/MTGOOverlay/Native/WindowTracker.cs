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
        // Cache handle for 2 seconds to avoid frequent process enumeration
        if (_mtgoWindowHandle != IntPtr.Zero && (DateTime.UtcNow - _lastSearchTime).TotalSeconds < 2.0)
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
        // 1. Fast check: is foreground window belonging to MTGO process?
        var foreground = Win32.GetForegroundWindow();
        if (foreground != IntPtr.Zero && IsWindowValid(foreground))
        {
            try
            {
                Win32.GetWindowThreadProcessId(foreground, out uint pid);
                var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("MTGO", StringComparison.OrdinalIgnoreCase))
                {
                    return foreground;
                }
            }
            catch { }
        }

        // 2. Search visible top-level windows for MTGO process
        IntPtr found = IntPtr.Zero;
        try
        {
            var processes = Process.GetProcessesByName("MTGO");
            if (processes.Length > 0)
            {
                var pids = new System.Collections.Generic.HashSet<uint>();
                foreach (var p in processes) pids.Add((uint)p.Id);

                Win32.EnumWindows((hWnd, lParam) =>
                {
                    if (Win32.IsWindowVisible(hWnd) && IsWindowValid(hWnd))
                    {
                        Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                        if (pids.Contains(pid))
                        {
                            found = hWnd;
                            return false; // Found main MTGO window, stop enumeration
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }
        catch { }

        return found;
    }
}
