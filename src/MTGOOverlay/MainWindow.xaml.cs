using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
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
                _trayIcon?.ShowBalloon("模式切换", $"已切换至：{GetModeDisplayName(mode)}");
            },
            onCalibrateRequested: OpenCalibration,
            onToggleLockPosition: () =>
            {
                if (_config.LockPosition)
                {
                    _config.FixedLeft = Left;
                    _config.FixedTop = Top;
                    _config.Save();
                    _trayIcon?.ShowBalloon("位置锁定", "当前浮窗位置已锁定，将不再跟随鼠标移动。");
                }
                else
                {
                    _trayIcon?.ShowBalloon("解除锁定", "浮窗将恢复动态定位。");
                }
            },
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
        if (_isScanning || !_cardDatabase.IsLoaded || !_ocrEngine.IsAvailable || !_isWebViewReady) return;

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
            // Prompt user once if preview rect hasn't been calibrated
            return;
        }

        var previewRect = _config.GetPreviewAbsoluteRect(mtgoRect);
        using var titleBmp = ScreenCapture.CapturePreviewTitle(previewRect);
        if (titleBmp == null) return;

        ulong currentHash = ScreenCapture.ComputeFastSampleHash(titleBmp);
        if (currentHash == _lastPreviewHash)
        {
            // Preview content hasn't changed; avoid duplicate OCR
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
                    // Position beside preview pane
                    var dpi = VisualTreeHelper.GetDpi(this);
                    double dipLeft = (previewRect.Right + 12) / dpi.DpiScaleX;
                    double dipTop = previewRect.Top / dpi.DpiScaleY;
                    PositionAndShow(card, dipLeft, dipTop);
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
            using var bmp = ScreenCapture.CaptureNearCursor(cursorPos, width: 340, height: 70, offsetY: -35);
            if (bmp == null) return;

            string rawOcr = await _ocrEngine.RecognizeTextAsync(bmp);
            if (string.IsNullOrWhiteSpace(rawOcr)) return;

            var card = _cardDatabase.Lookup(rawOcr);
            if (card != null)
            {
                if (card.NameEn != _currentCardName)
                {
                    _currentCardName = card.NameEn;

                    var dpi = VisualTreeHelper.GetDpi(this);
                    double dipX = cursorPos.X / dpi.DpiScaleX;
                    double dipY = cursorPos.Y / dpi.DpiScaleY;

                    PositionAndShow(card, dipX + 24, dipY - 40);
                }
            }
        }
        finally
        {
            _isScanning = false;
        }
    }

    private void PositionAndShow(CardEntry card, double preferredLeft, double preferredTop)
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        double left = preferredLeft;
        double top = preferredTop;

        if (_config.LockPosition)
        {
            left = _config.FixedLeft;
            top = _config.FixedTop;
        }
        else
        {
            if (left + Width > screenWidth - 10)
            {
                left = Math.Max(10, preferredLeft - Width - 48);
            }
            if (top + Height > screenHeight - 10)
            {
                top = Math.Max(10, screenHeight - Height - 10);
            }
            if (top < 10) top = 10;
        }

        Left = left;
        Top = top;
        Visibility = Visibility.Visible;

        var payload = JsonSerializer.Serialize(new
        {
            action = "showCard",
            card = card
        });

        OverlayWebView.CoreWebView2?.PostWebMessageAsJson(payload);
    }

    private void HideOverlay()
    {
        _currentCardName = null;
        _lastPreviewHash = 0;
        Visibility = Visibility.Hidden;
        OverlayWebView.CoreWebView2?.PostWebMessageAsJson("{\"action\":\"clear\"}");
    }

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

        var dpi = VisualTreeHelper.GetDpi(this);
        double left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        double top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        PositionAndShow(sampleCard, left, top);
    }

    private static string GetModeDisplayName(TriggerMode mode) => mode switch
    {
        TriggerMode.PreviewPane => "Card Preview 停靠窗锚定",
        TriggerMode.HoldCtrl => "按住 Ctrl 键取词",
        _ => "光标悬停取词"
    };

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
