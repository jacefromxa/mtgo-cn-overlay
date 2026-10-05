using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
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

    private AppConfig _config = new();
    private TrayIconManager? _trayIcon;

    private System.Drawing.Point _lastMousePos;
    private DateTime _mouseStationaryStart = DateTime.MinValue;
    private string? _currentCardName;
    private ulong _lastPreviewHash;

    private CancellationTokenSource? _trackerCts;
    private int _scanInProgress = 0;
    private volatile bool _isWebViewReady;
    private volatile bool _isMoveModeActive;

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
                _trackerCts?.Cancel();
                _trayIcon?.Dispose();
                System.Windows.Application.Current.Shutdown();
            }
        );

        // 1. Initialize WebView2 with isolated LocalAppData directory to prevent cloud sync (e.g. Baidu Netdisk) file locks
        try
        {
            string webFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
            if (!Directory.Exists(webFolder))
            {
                webFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "web"));
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string webViewDataDir = Path.Combine(localAppData, "MTGOOverlay", "WebView2Data");
            Directory.CreateDirectory(webViewDataDir);

            var env = await CoreWebView2Environment.CreateAsync(null, webViewDataDir);
            await OverlayWebView.EnsureCoreWebView2Async(env);

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

        // 2. Initialize OCR & Card Database in background
        _ = Task.Run(async () =>
        {
            _ocrEngine.Initialize();
            try
            {
                await _cardDatabase.LoadDatabaseAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load card DB: {ex.Message}");
            }
        });

        // 3. Start dedicated background tracking loop (off UI thread)
        StartBackgroundTracker();
    }

    private void StartBackgroundTracker()
    {
        _trackerCts?.Cancel();
        _trackerCts = new CancellationTokenSource();
        var token = _trackerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(80, token);

                    if (_isMoveModeActive || !_cardDatabase.IsLoaded || !_ocrEngine.IsAvailable || !_isWebViewReady)
                    {
                        continue;
                    }

                    // Ensure single flight: skip if a scan is already in progress
                    if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        await DoBackgroundTrackStepAsync();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _scanInProgress, 0);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background tracker error: {ex.Message}");
                }
            }
        }, token);
    }

    private async Task DoBackgroundTrackStepAsync()
    {
        switch (_config.Mode)
        {
            case TriggerMode.PreviewPane:
                await HandlePreviewPaneBackgroundAsync();
                break;

            case TriggerMode.HoldCtrl:
                await HandleHoldCtrlBackgroundAsync();
                break;

            case TriggerMode.Hover:
            default:
                await HandleHoverBackgroundAsync();
                break;
        }
    }

    private async Task HandlePreviewPaneBackgroundAsync()
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
            // Preview content unchanged; skip OCR
            return;
        }

        string rawOcr = await _ocrEngine.RecognizeTextAsync(titleBmp);
        if (string.IsNullOrWhiteSpace(rawOcr)) return;

        var card = _cardDatabase.Lookup(rawOcr);
        if (card != null)
        {
            _lastPreviewHash = currentHash;
            if (card.NameEn != _currentCardName)
            {
                _currentCardName = card.NameEn;

                Win32.GetCursorPos(out var pt);
                var currentCursor = new System.Drawing.Point(pt.X, pt.Y);

                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    PositionAndShow(card, currentCursor, previewRect);
                }));
            }
        }
    }

    private async Task HandleHoldCtrlBackgroundAsync()
    {
        bool isCtrlDown = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
        if (!isCtrlDown)
        {
            if (_currentCardName != null)
            {
                await Dispatcher.BeginInvoke(new Action(HideOverlay));
            }
            return;
        }

        await HandleHoverBackgroundAsync();
    }

    private async Task HandleHoverBackgroundAsync()
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
                await Dispatcher.BeginInvoke(new Action(HideOverlay));
            }
            return;
        }

        var stationaryDuration = DateTime.UtcNow - _mouseStationaryStart;
        if (stationaryDuration.TotalMilliseconds < _config.DebounceMs) return;

        if (!_windowTracker.IsCursorInsideMtgo(out var cursorPos, out _))
        {
            return;
        }

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
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    PositionAndShow(card, cursorPos, null);
                }));
            }
        }
    }

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

    private void CalculateSmartCursorPosition(System.Drawing.Point cursorPos, out double left, out double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double curX = cursorPos.X / dpi.DpiScaleX;
        double curY = cursorPos.Y / dpi.DpiScaleY;

        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        double overlayWidth = Width;
        double overlayHeight = Height;
        double offsetH = 26;
        double offsetV = 36;

        // Auto flip left / right
        if (curX + offsetH + overlayWidth <= screenWidth - 15)
        {
            left = curX + offsetH;
        }
        else
        {
            left = curX - overlayWidth - offsetH;
        }

        if (left < 10) left = 10;

        top = curY - offsetV;
        if (top + overlayHeight > screenHeight - 15)
        {
            top = Math.Max(10, screenHeight - overlayHeight - 15);
        }
        if (top < 10) top = 10;
    }

    private void CalculateBesidePreviewPosition(Rectangle previewRect, out double left, out double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        double prevRight = previewRect.Right / dpi.DpiScaleX;
        double prevLeft = previewRect.Left / dpi.DpiScaleX;
        double prevTop = previewRect.Top / dpi.DpiScaleY;

        if (prevRight + 12 + Width <= screenWidth - 15)
        {
            left = prevRight + 12;
        }
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

    #region Custom Position Dragging Handle

    public void StartMoveOverlayMode()
    {
        _isMoveModeActive = true;
        ShowTestCard();

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

        _config.FixedLeft = Left;
        _config.FixedTop = Top;
        _config.PositionMode = OverlayPositionMode.CustomFixed;
        _config.Save();

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
        _trackerCts?.Cancel();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
