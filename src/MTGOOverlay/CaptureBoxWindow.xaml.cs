using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MTGOOverlay.Models;
using MTGOOverlay.Native;

namespace MTGOOverlay;

public partial class CaptureBoxWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<Rectangle>? _onBoxChanged;
    private readonly Action<bool>? _onLockChanged;
    private readonly Action? _onOpenSettingsRequested;

    private bool _isInitialized;
    private Rectangle _cachedPhysicalCaptureRect;

    public bool IsLocked { get; private set; }
    public volatile bool IsResizing;
    public volatile bool IsBoxVisible = true;

    public Rectangle CachedPhysicalCaptureRect
    {
        get
        {
            var r = _cachedPhysicalCaptureRect;
            if (r.Width > 0 && r.Height > 0) return r;
            return _config.GetCaptureBoxRect();
        }
        private set => _cachedPhysicalCaptureRect = value;
    }

    public CaptureBoxWindow(
        AppConfig config,
        Action<Rectangle>? onBoxChanged = null,
        Action<bool>? onLockChanged = null,
        Action? onOpenSettingsRequested = null)
    {
        InitializeComponent();
        _config = config;
        _onBoxChanged = onBoxChanged;
        _onLockChanged = onLockChanged;
        _onOpenSettingsRequested = onOpenSettingsRequested;
        IsLocked = config.CaptureBoxLocked;
    }

    private IntPtr _hwnd = IntPtr.Zero;
    private volatile bool _isTopmost = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(_hwnd, clickThrough: IsLocked);
        Win32.ExcludeWindowAndChildrenFromCapture(_hwnd);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetOverlayWindowStyles(_hwnd, clickThrough: IsLocked);
            Win32.ExcludeWindowAndChildrenFromCapture(_hwnd);
        }

        // Apply saved absolute desktop coordinates
        var absRect = _config.GetCaptureBoxRect();
        _cachedPhysicalCaptureRect = absRect;
        SetPhysicalCaptureRect(absRect);
        ApplyLockVisualState(IsLocked);
        EnsureTopmost();

        IsBoxVisible = true;
        _isInitialized = true;
    }

    private DpiScale GetDpiSafe()
    {
        try
        {
            return VisualTreeHelper.GetDpi(this);
        }
        catch
        {
            return new DpiScale(1.0, 1.0);
        }
    }

    public void EnsureTopmost()
    {
        IntPtr hWnd = _hwnd;
        if (hWnd == IntPtr.Zero && Dispatcher.CheckAccess())
        {
            hWnd = _hwnd = new WindowInteropHelper(this).Handle;
        }

        if (hWnd != IntPtr.Zero)
        {
            _isTopmost = true;
            Win32.SetWindowPos(hWnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }
    }

    public void DemoteFromTopmost()
    {
        IntPtr hWnd = _hwnd;
        if (hWnd == IntPtr.Zero && Dispatcher.CheckAccess())
        {
            hWnd = _hwnd = new WindowInteropHelper(this).Handle;
        }

        if (hWnd != IntPtr.Zero && _isTopmost)
        {
            _isTopmost = false;
            Win32.SetWindowPos(hWnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        EnsureTopmost();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
    }

    public Rectangle GetPhysicalCaptureRect()
    {
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        if (hWnd != IntPtr.Zero && Win32.GetWindowRect(hWnd, out var r))
        {
            var winRect = r.ToRectangle();
            if (IsLocked)
            {
                return winRect;
            }
            else
            {
                var dpi = GetDpiSafe();
                int headerPhysH = (int)Math.Round(26 * dpi.DpiScaleY);
                return new Rectangle(
                    winRect.Left,
                    winRect.Top + headerPhysH,
                    winRect.Width,
                    Math.Max(12, winRect.Height - headerPhysH));
            }
        }

        var d = GetDpiSafe();
        double capLeft = Left;
        double capTop = IsLocked ? Top : Top + 26;
        double capWidth = Width;
        double capHeight = IsLocked ? Height : Math.Max(12, Height - 26);

        return new Rectangle(
            (int)Math.Round(capLeft * d.DpiScaleX),
            (int)Math.Round(capTop * d.DpiScaleY),
            (int)Math.Round(capWidth * d.DpiScaleX),
            (int)Math.Round(capHeight * d.DpiScaleY));
    }

    public void SetPhysicalCaptureRect(Rectangle physRect)
    {
        if (physRect.Width <= 0 || physRect.Height <= 0) return;

        var dpi = GetDpiSafe();
        int headerPhysH = IsLocked ? 0 : (int)Math.Round(26 * dpi.DpiScaleY);
        int targetX = physRect.Left;
        int targetY = physRect.Top - headerPhysH;
        int targetW = Math.Max(80, physRect.Width);
        int targetH = Math.Max(16 + headerPhysH, physRect.Height + headerPhysH);

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        if (hWnd != IntPtr.Zero)
        {
            Win32.SetWindowPos(hWnd, Win32.HWND_TOPMOST, targetX, targetY, targetW, targetH,
                Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }

        Left = targetX / dpi.DpiScaleX;
        Top = targetY / dpi.DpiScaleY;
        Width = targetW / dpi.DpiScaleX;
        Height = targetH / dpi.DpiScaleY;

        CachedPhysicalCaptureRect = physRect;
        UpdateDimensionsDisplay();
    }

    public void SetLocked(bool locked)
    {
        if (IsLocked == locked && _isInitialized) return;

        var currentPhys = GetPhysicalCaptureRect();
        IsLocked = locked;

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        if (hWnd != IntPtr.Zero)
        {
            Win32.SetOverlayWindowStyles(hWnd, clickThrough: locked);
        }

        ApplyLockVisualState(locked);
        SetPhysicalCaptureRect(currentPhys);
        EnsureTopmost();

        _config.CaptureBoxLocked = locked;
        _config.Save();

        _onLockChanged?.Invoke(locked);
    }

    public void ApplyBorderVisibility(bool showBorder)
    {
        _config.ShowCaptureBoxBorder = showBorder;
        _config.Save();

        if (IsLocked)
        {
            if (showBorder)
            {
                CaptureBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x75, 0xFF, 0xAD, 0x42));
                CaptureBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                CaptureBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
                CaptureBorder.BorderThickness = new Thickness(0);
            }
        }
    }

    private void ApplyLockVisualState(bool locked)
    {
        if (locked)
        {
            HeaderRow.Height = new GridLength(0);
            HeaderBar.Visibility = Visibility.Collapsed;
            ResizeGripsPanel.Visibility = Visibility.Collapsed;
            TxtGuide.Visibility = Visibility.Collapsed;

            CaptureBorder.Cursor = System.Windows.Input.Cursors.Arrow;
            CaptureBorder.Background = System.Windows.Media.Brushes.Transparent;
            CaptureBorder.CornerRadius = new CornerRadius(2);

            if (_config.ShowCaptureBoxBorder)
            {
                CaptureBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x75, 0xFF, 0xAD, 0x42));
                CaptureBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                CaptureBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
                CaptureBorder.BorderThickness = new Thickness(0);
            }
        }
        else
        {
            HeaderRow.Height = new GridLength(26);
            HeaderBar.Visibility = Visibility.Visible;
            ResizeGripsPanel.Visibility = Visibility.Visible;
            TxtGuide.Visibility = Visibility.Visible;

            CaptureBorder.Cursor = System.Windows.Input.Cursors.SizeAll;
            CaptureBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0C, 0xFF, 0xAD, 0x42));
            CaptureBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xAD, 0x42));
            CaptureBorder.BorderThickness = new Thickness(2);
            CaptureBorder.CornerRadius = new CornerRadius(0, 0, 3, 3);
        }
    }

    public void UpdateDimensionsDisplay()
    {
        var dpi = GetDpiSafe();
        int w = (int)Math.Round(Width * dpi.DpiScaleX);
        double capH = IsLocked ? Height : Math.Max(12, Height - 26);
        int h = (int)Math.Round(capH * dpi.DpiScaleY);
        TxtSize.Text = $"{w} × {h}";
    }

    public void UpdateOcrFeedback(string rawOcr, CardEntry? card, long elapsedMs)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateOcrFeedback(rawOcr, card, elapsedMs)));
            return;
        }

        if (card != null)
        {
            TxtFeedback.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80)); // Green
            TxtFeedback.Text = $"⚡ {card.NameZh} ({card.NameEn}) · {elapsedMs}ms";
        }
        else if (!string.IsNullOrWhiteSpace(rawOcr))
        {
            TxtFeedback.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)); // Amber
            string shortOcr = rawOcr.Replace("\r", " ").Replace("\n", " ").Trim();
            if (shortOcr.Length > 20) shortOcr = shortOcr.Substring(0, 20) + "…";
            TxtFeedback.Text = $"❓ \"{shortOcr}\" · {elapsedMs}ms";
        }
        else
        {
            TxtFeedback.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)); // Gray
            TxtFeedback.Text = "⚪ 等待预览...";
        }
    }

    public void SyncWithMtgoWindow(Rectangle mtgoRect)
    {
        // Window auto-tracking is disabled per user request to prevent erratic jumping.
    }

    private void NotifyBoxChanged()
    {
        UpdateDimensionsDisplay();
        var physRect = GetPhysicalCaptureRect();
        CachedPhysicalCaptureRect = physRect;
        _config.CaptureBoxX = physRect.Left;
        _config.CaptureBoxY = physRect.Top;
        _config.CaptureBoxWidth = physRect.Width;
        _config.CaptureBoxHeight = physRect.Height;
        _config.Save();

        _onBoxChanged?.Invoke(physRect);
    }

    #region Interaction Handlers

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        _onOpenSettingsRequested?.Invoke();
    }

    private void BtnLock_Click(object sender, RoutedEventArgs e)
    {
        SetLocked(true);
    }

    private void HeaderBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                IsResizing = true;
                DragMove();
            }
            catch { }
            finally
            {
                IsResizing = false;
                EnsureTopmost();
                NotifyBoxChanged();
            }
        }
    }

    private void CaptureBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                IsResizing = true;
                DragMove();
            }
            catch { }
            finally
            {
                IsResizing = false;
                EnsureTopmost();
                NotifyBoxChanged();
            }
        }
    }

    #endregion

    #region Resize Thumbs

    private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        IsResizing = true;
    }

    private void ThumbRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        try
        {
            IsResizing = true;
            if (double.IsNaN(e.HorizontalChange)) return;
            Width = Math.Clamp(Width + e.HorizontalChange, 80, 2400);
            UpdateDimensionsDisplay();
        }
        catch { }
    }

    private void ThumbBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        try
        {
            IsResizing = true;
            if (double.IsNaN(e.VerticalChange)) return;
            double minH = IsLocked ? 16 : 42;
            Height = Math.Clamp(Height + e.VerticalChange, minH, 1600);
            UpdateDimensionsDisplay();
        }
        catch { }
    }

    private void ThumbLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        try
        {
            IsResizing = true;
            if (double.IsNaN(e.HorizontalChange)) return;
            double delta = e.HorizontalChange;
            if (Width - delta >= 80 && Width - delta <= 2400)
            {
                Left += delta;
                Width -= delta;
                UpdateDimensionsDisplay();
            }
        }
        catch { }
    }

    private void ThumbTop_DragDelta(object sender, DragDeltaEventArgs e)
    {
        try
        {
            IsResizing = true;
            if (double.IsNaN(e.VerticalChange)) return;
            double delta = e.VerticalChange;
            double minH = IsLocked ? 16 : 42;
            if (Height - delta >= minH && Height - delta <= 1600)
            {
                Top += delta;
                Height -= delta;
                UpdateDimensionsDisplay();
            }
        }
        catch { }
    }

    private void ThumbTopLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ThumbTop_DragDelta(sender, e);
        ThumbLeft_DragDelta(sender, e);
    }

    private void ThumbTopRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ThumbTop_DragDelta(sender, e);
        ThumbRight_DragDelta(sender, e);
    }

    private void ThumbBottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ThumbBottom_DragDelta(sender, e);
        ThumbLeft_DragDelta(sender, e);
    }

    private void ThumbBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ThumbBottom_DragDelta(sender, e);
        ThumbRight_DragDelta(sender, e);
    }

    private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        IsResizing = false;
        EnsureTopmost();
        NotifyBoxChanged();
    }

    #endregion
}
