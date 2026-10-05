using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MTGOOverlay.Capture;
using MTGOOverlay.Data;
using MTGOOverlay.Models;
using MTGOOverlay.Native;
using MTGOOverlay.Ocr;

namespace MTGOOverlay;

public partial class MainWindow : Window
{
    private readonly WindowTracker _windowTracker = new();
    private readonly WindowsOcrEngine _ocrEngine = new();
    private readonly CardDatabase _cardDatabase = new();
    private readonly DispatcherTimer _trackerTimer = new();

    private AppConfig _config = new();
    private TrayIconManager? _trayIcon;

    private System.Drawing.Point _lastMousePos;
    private DateTime _mouseStationaryStart = DateTime.MinValue;
    private string? _currentCardName;
    private ulong _lastPreviewHash;
    private bool _isScanning;
    private bool _isWebViewReady;
    private bool _isMoveModeActive;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _config = AppConfig.Load();
        Width = _config.Width;
        Height = _config.Height;

        // Initialize System Tray Icon
        _trayIcon = new TrayIconManager(
            _config,
            onModeChanged: mode =>
            {
                _trayIcon?.ShowBalloon("取词模式切换", $"已切换至：{GetModeDisplayName(mode)}");
            },
            onPositionModeChanged: posMode =>
            {
                _trayIcon?.ShowBalloon("位置模式切换", $"已切换至：{GetPositionModeDisplayName(posMode)}");
            },
            onStartMoveOverlay: StartMoveOverlayMode,
            onCalibrateRequested: OpenCalibration,
            onTestCardRequested: ShowTestCard,
            onOpacityChanged: op =>
            {
                ApplyOpacity(op);
            },
            onExitRequested: () =>
            {
                _trayIcon?.Dispose();
                System.Windows.Application.Current.Shutdown();
            }
        );

        // Initialize WebView2
        string webFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        if (!Directory.Exists(webFolder))
        {
            webFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "web"));
        }

        try
        {
            await OverlayWebView.EnsureCoreWebView2Async();
            OverlayWebView.CoreWebView2.WebMessageReceived += (s, args) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                    if (doc.RootElement.TryGetProperty("status", out var st) && st.GetString() == "ready")
                    {
                        _isWebViewReady = true;
                        ApplyOpacity(_config.Opacity);
                    }
                }
                catch { }
            };

            string indexPath = Path.Combine(webFolder, "index.html");
            if (File.Exists(indexPath))
            {
                OverlayWebView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");
        }

        // Initialize OCR & Card Database
        _ocrEngine.Initialize();
        _ = Task.Run(async () =>
        {
            try
            {
                await _cardDatabase.LoadDatabaseAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load card DB: {ex.Message}");
            }
        });

        // Start tracking timer
        _trackerTimer.Interval = TimeSpan.FromMilliseconds(80);
        _trackerTimer.Tick += OnTrackerTick;
        _trackerTimer.Start();
    }

    private void ApplyOpacity(double opacity)
    {
        if (OverlayWebView.CoreWebView2 == null) return;
        var msg = JsonSerializer.Serialize(new
        {
            action = "settings",
            settings = new { bgOpacity = opacity }
        });
        OverlayWebView.CoreWebView2.PostWebMessageAsJson(msg);
    }

    private async void OnTrackerTick(object? sender, EventArgs e)
    {
        if (_isMoveModeActive || _isScanning || !_cardDatabase.IsLoaded || !_ocrEngine.IsAvailable || !_isWebViewReady) return;

        switch (_config.Mode)
        {
            case TriggerMode.PreviewPane:
                await HandlePreviewPaneModeAsync();
                break;

            case TriggerMode.HoldCtrl:
                await HandleHoldCtrlModeAsync();
                break;

            case TriggerMode.Hover:
            default:
                await HandleHoverModeAsync();
                break;
        }
    }

    private async Task HandlePreviewPaneModeAsync()
    {
        IntPtr hWnd = _windowTracker.GetMtgoWindowHandle();
        if (hWnd == IntPtr.Zero || !Win32.GetWindowRect(hWnd, out var rect))
        {
            return;
        }

        var mtgoRect = rect.ToRectangle();
        if (!_config.HasValidPreviewRect)
        {
            return;
        }

        var previewRect = _config.GetPreviewAbsoluteRect(mtgoRect);
        using var titleBmp = ScreenCapture.CapturePreviewTitle(previewRect);
        if (titleBmp == null) return;

        ulong currentHash = ScreenCapture.ComputeFastSampleHash(titleBmp);
        if (currentHash == _lastPreviewHash)
        {
            // Preview content hasn't changed; avoid redundant OCR
            return;
        }

        _isScanning = true;
        try
        {
            string rawOcr = await _ocrEngine.RecognizeTextAsync(titleBmp);
            if (string.IsNullOrWhiteSpace(rawOcr)) return;

            var card = _cardDatabase.Lookup(rawOcr);
            if (card != null)
            {
                _lastPreviewHash = currentHash;
                if (card.NameEn != _currentCardName)
                {
                    _currentCardName = card.NameEn;

                    // Get current cursor for smart position follow
                    Win32.GetCursorPos(out var pt);
                    var currentCursor = new System.Drawing.Point(pt.X, pt.Y);

                    PositionAndShow(card, currentCursor, previewRect);
                }
            }
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task HandleHoldCtrlModeAsync()
    {
        bool isCtrlDown = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
        if (!isCtrlDown)
        {
            if (_currentCardName != null)
            {
                HideOverlay();
            }
            return;
        }

        await HandleHoverModeAsync();
    }

    private async Task HandleHoverModeAsync()
    {
        if (!Win32.GetCursorPos(out var pt)) return;
        var currentPos = new System.Drawing.Point(pt.X, pt.Y);

        int deltaX = Math.Abs(currentPos.X - _lastMousePos.X);
        int deltaY = Math.Abs(currentPos.Y - _lastMousePos.Y);

        if (deltaX > 4 || deltaY > 4)
        {
            _lastMousePos = currentPos;
            _mouseStationaryStart = DateTime.UtcNow;

            if (_currentCardName != null && (deltaX > 90 || deltaY > 90))
            {
                HideOverlay();
            }
            return;
        }

        var stationaryDuration = DateTime.UtcNow - _mouseStationaryStart;
        if (stationaryDuration.TotalMilliseconds < _config.DebounceMs) return;

        if (!_windowTracker.IsCursorInsideMtgo(out var cursorPos, out _))
        {
            return;
        }

        _isScanning = true;
        try
        {
            // Capture full-card area (height 220, covering -160px upward to ensure top title bar is included)
            using var bmp = ScreenCapture.CaptureNearCursor(cursorPos, width: 360, height: 220, offsetY: -160);
            if (bmp == null) return;

            string rawOcr = await _ocrEngine.RecognizeTextAsync(bmp);
            if (string.IsNullOrWhiteSpace(rawOcr)) return;

            var card = _cardDatabase.Lookup(rawOcr);
            if (card != null)
            {
                if (card.NameEn != _currentCardName)
                {
                    _currentCardName = card.NameEn;
                    PositionAndShow(card, cursorPos, null);
                }
            }
        }
        finally
        {
            _isScanning = false;
        }
    }

    /// <summary>
    /// Positions and displays overlay according to user's configured PositionMode with screen edge auto-flipping.
    /// </summary>
    private void PositionAndShow(CardEntry card, System.Drawing.Point cursorPos, Rectangle? previewRect)
    {
        double targetLeft;
        double targetTop;

        switch (_config.PositionMode)
        {
            case OverlayPositionMode.CustomFixed:
                targetLeft = _config.FixedLeft;
                targetTop = _config.FixedTop;
                break;

            case OverlayPositionMode.BesidePreview when previewRect.HasValue:
                CalculateBesidePreviewPosition(previewRect.Value, out targetLeft, out targetTop);
                break;

            case OverlayPositionMode.FollowCursor:
            default:
                CalculateSmartCursorPosition(cursorPos, out targetLeft, out targetTop);
                break;
        }

        Left = targetLeft;
        Top = targetTop;
        Visibility = Visibility.Visible;

        var payload = JsonSerializer.Serialize(new
        {
            action = "showCard",
            card = card
        });

        OverlayWebView.CoreWebView2?.PostWebMessageAsJson(payload);
    }

    /// <summary>
    /// Intelligently calculates position near cursor, automatically flipping to the left or right of the mouse
    /// depending on available screen width.
    /// </summary>
    private void CalculateSmartCursorPosition(System.Drawing.Point cursorPos, out double left, out double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double curX = cursorPos.X / dpi.DpiScaleX;
        double curY = cursorPos.Y / dpi.DpiScaleY;

        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        double overlayWidth = Width;
        double overlayHeight = Height;
        double offsetH = 26; // Distance from cursor
        double offsetV = 36; // Distance above cursor

        // 1. Horizontal: Check available space on the right side
        // If enough room on the right (with at least 15px safety margin from right edge):
        if (curX + offsetH + overlayWidth <= screenWidth - 15)
        {
            left = curX + offsetH;
        }
        // Otherwise, automatically flip to the LEFT of the cursor!
        else
        {
            left = curX - overlayWidth - offsetH;
        }

        // Clamp left edge so it never goes off-screen
        if (left < 10)
        {
            left = 10;
        }

        // 2. Vertical: align slightly above cursor, but keep inside screen
        top = curY - offsetV;
        if (top + overlayHeight > screenHeight - 15)
        {
            top = Math.Max(10, screenHeight - overlayHeight - 15);
        }
        if (top < 10)
        {
            top = 10;
        }
    }

    /// <summary>
    /// Positions overlay beside the MTGO Card Preview pane.
    /// </summary>
    private void CalculateBesidePreviewPosition(Rectangle previewRect, out double left, out double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        double prevRight = previewRect.Right / dpi.DpiScaleX;
        double prevLeft = previewRect.Left / dpi.DpiScaleX;
        double prevTop = previewRect.Top / dpi.DpiScaleY;

        // Try right side first
        if (prevRight + 12 + Width <= screenWidth - 15)
        {
            left = prevRight + 12;
        }
        // Otherwise, place to the left of the preview dock
        else if (prevLeft - 12 - Width >= 15)
        {
            left = prevLeft - 12 - Width;
        }
        else
        {
            left = Math.Max(10, screenWidth - Width - 15);
        }

        top = Math.Max(10, prevTop);
        if (top + Height > screenHeight - 15)
        {
            top = Math.Max(10, screenHeight - Height - 15);
        }
    }

    private void HideOverlay()
    {
        if (_isMoveModeActive) return;

        _currentCardName = null;
        _lastPreviewHash = 0;
        Visibility = Visibility.Hidden;
        OverlayWebView.CoreWebView2?.PostWebMessageAsJson("{\"action\":\"clear\"}");
    }

    #region Custom Position Dragging Handle

    public void StartMoveOverlayMode()
    {
        _isMoveModeActive = true;
        ShowTestCard();

        // Make window interactive (not click-through)
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: false);

        MoveBar.Visibility = Visibility.Visible;
        _trayIcon?.ShowBalloon("调整位置模式", "请按住顶部的金色横条拖拽浮窗至理想位置，随后点击「保存并锁定」。");
    }

    private void MoveBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void LockCurrentPosition_Click(object sender, RoutedEventArgs e)
    {
        _isMoveModeActive = false;
        MoveBar.Visibility = Visibility.Collapsed;

        // Save position
        _config.FixedLeft = Left;
        _config.FixedTop = Top;
        _config.PositionMode = OverlayPositionMode.CustomFixed;
        _config.Save();

        // Restore click-through
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: true);

        _trayIcon?.RefreshPositionUI(OverlayPositionMode.CustomFixed);
        _trayIcon?.ShowBalloon("位置已保存并锁定", $"浮窗位置已锁定至坐标 ({Math.Round(Left)}, {Math.Round(Top)})。随时可通过托盘菜单恢复鼠标跟随。");
    }

    #endregion

    private void OpenCalibration()
    {
        var calibWindow = new CalibrationWindow(_windowTracker, _config, rect =>
        {
            _trayIcon?.RefreshModeUI(TriggerMode.PreviewPane);
            _trayIcon?.ShowBalloon("校准完成", "Card Preview 区域校准成功！已自动切换为 Preview 锚定模式。");
        });
        calibWindow.ShowDialog();
    }

    private void ShowTestCard()
    {
        var sampleCard = new CardEntry
        {
            NameZh = "闪电击",
            NameEn = "Lightning Bolt",
            ManaCost = "{R}",
            TypeLine = "瞬间",
            RulesText = "闪电击对任意一个目标造成3点伤害。",
            FlavorText = "「太棒了，这真不可思议！不过我可不想再经历第二次了。」——探险家贾米拉",
            Source = "本地字典 (测试)"
        };

        if (_config.PositionMode == OverlayPositionMode.CustomFixed)
        {
            Left = _config.FixedLeft;
            Top = _config.FixedTop;
        }
        else
        {
            Win32.GetCursorPos(out var pt);
            CalculateSmartCursorPosition(new System.Drawing.Point(pt.X, pt.Y), out double l, out double t);
            Left = l;
            Top = t;
        }

        Visibility = Visibility.Visible;

        var payload = JsonSerializer.Serialize(new
        {
            action = "showCard",
            card = sampleCard
        });

        OverlayWebView.CoreWebView2?.PostWebMessageAsJson(payload);
    }

    private static string GetModeDisplayName(TriggerMode mode) => mode switch
    {
        TriggerMode.PreviewPane => "Card Preview 停靠窗锚定",
        TriggerMode.HoldCtrl => "按住 Ctrl 键取词",
        _ => "光标悬停取词"
    };

    private static string GetPositionModeDisplayName(OverlayPositionMode posMode) => posMode switch
    {
        OverlayPositionMode.BesidePreview => "停靠在 Card Preview 预览窗旁",
        OverlayPositionMode.CustomFixed => "固定在自定义坐标",
        _ => "智能追随鼠标 (自适应左右侧)"
    };

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
