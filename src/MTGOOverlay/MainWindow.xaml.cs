using System;
using System.Diagnostics;
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
    private readonly IOcrEngine _ocrEngine = new HybridOcrEngine();
    private readonly CardDatabase _cardDatabase = new();

    private AppConfig _config = new();
    private TrayIconManager? _trayIcon;
    private CaptureBoxWindow? _captureBoxWindow;
    private SettingsWindow? _settingsWindow;

    private IntPtr _myHwnd = IntPtr.Zero;
    private volatile bool _isOverlayVisible;

    private string? _currentCardName;
    private CardEntry? _currentCard;
    private CardEntry? _pendingCard;
    private ulong _lastPreviewHash;
    private System.Drawing.Point _lastCursorPos;
    private int _nullCardCount = 0;
    private DateTime _lastClickTime = DateTime.MinValue;

    private CancellationTokenSource? _trackerCts;
    private int _scanInProgress = 0;
    private volatile bool _isWebViewReady;
    private volatile bool _isMoveModeActive;

    public MainWindow()
    {
        App.Log("MainWindow constructor start");
        InitializeComponent();
        App.Log("MainWindow constructor done");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        App.Log("OnSourceInitialized start");
        _myHwnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(_myHwnd, clickThrough: true);
        Win32.ExcludeWindowAndChildrenFromCapture(_myHwnd);
        App.Log("OnSourceInitialized done");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log("Window_Loaded start");
        _config = AppConfig.Load();
        Width = _config.Width;
        Height = _config.Height;

        // Initialize Settings Control Window
        _settingsWindow = new SettingsWindow(
            _config,
            onCtrlModifierChanged: require =>
            {
                _trayIcon?.RefreshCtrlModifierUI(require);
            },
            onPosModeChanged: posMode =>
            {
                _trayIcon?.RefreshPositionUI(posMode);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_isMoveModeActive)
                    {
                        _isMoveModeActive = false;
                        MoveBar.Visibility = Visibility.Collapsed;
                        Height = _config.Height;
                        IntPtr hWnd = new WindowInteropHelper(this).Handle;
                        Win32.SetOverlayWindowStyles(hWnd, clickThrough: true);
                    }
                }));
            },
            onUnlockCaptureBox: () => OpenCaptureBoxAdjustMode(),
            onLockCaptureBox: () => ToggleCaptureBoxLock(true),
            onToggleBorder: show => _captureBoxWindow?.ApplyBorderVisibility(show),
            onCalibrateFull: () => OpenCalibration(),
            onMoveOverlay: () => StartMoveOverlayMode(),
            onStyleChanged: () => ApplyStyleSettings(),
            onTestCaptureRequested: async () => await RunTestOcrCaptureAsync(),
            onTriggerModeChanged: mode => HandleTriggerModeChanged(mode)
        );

        // Initialize Interactive Capture Box Window
        _captureBoxWindow = new CaptureBoxWindow(
            _config,
            onBoxChanged: physRect =>
            {
                _config.CaptureBoxX = physRect.Left;
                _config.CaptureBoxY = physRect.Top;
                _config.CaptureBoxWidth = physRect.Width;
                _config.CaptureBoxHeight = physRect.Height;
                _config.Save();
                _settingsWindow?.UpdateCaptureBoxStatusText();
                _lastPreviewHash = 0; // Trigger OCR rescan immediately
            },
            onLockChanged: locked =>
            {
                _trayIcon?.RefreshCaptureBoxLocked(locked);
                if (locked)
                {
                    _trayIcon?.ShowBalloon("识别框已锁定", "卡牌识别框已切换为鼠标穿透模式。打牌点击不受影响，如需调整请右键托盘图标或在设置中点击「调整识别框」。");
                }
            },
            onOpenSettingsRequested: OpenSettingsWindow
        );

        if (_config.Mode == TriggerMode.Hover)
        {
            _captureBoxWindow.Hide();
        }
        else
        {
            _captureBoxWindow.Show();
        }

        // Initialize System Tray Icon
        _trayIcon = new TrayIconManager(
            _config,
            onCtrlModifierChanged: req =>
            {
                _settingsWindow?.RefreshCtrlModifier(req);
            },
            onPositionModeChanged: posMode =>
            {
                _trayIcon?.ShowBalloon("位置模式切换", $"已切换至：{GetPositionModeDisplayName(posMode)}");
                _settingsWindow?.RefreshPositionMode(posMode);
            },
            onStartMoveOverlay: StartMoveOverlayMode,
            onCalibrateRequested: OpenCalibration,
            onAdjustCaptureBoxRequested: OpenCaptureBoxAdjustMode,
            onLockCaptureBoxToggle: ToggleCaptureBoxLock,
            onToggleCaptureBoxBorder: showBorder => _captureBoxWindow?.ApplyBorderVisibility(showBorder),
            onOpenSettingsRequested: OpenSettingsWindow,
            onOpacityChanged: op =>
            {
                ApplyStyleSettings();
            },
            onExitRequested: () =>
            {
                _trackerCts?.Cancel();
                _captureBoxWindow?.Close();
                _settingsWindow?.Close();
                _trayIcon?.Dispose();
                System.Windows.Application.Current.Shutdown();
            },
            onTriggerModeChanged: mode => HandleTriggerModeChanged(mode)
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
                        Win32.ExcludeWindowAndChildrenFromCapture(_myHwnd);
                        ApplyStyleSettings();

                        if (_pendingCard != null)
                        {
                            var p = JsonSerializer.Serialize(new
                            {
                                action = "showCard",
                                card = _pendingCard
                            });
                            OverlayWebView.CoreWebView2.PostWebMessageAsJson(p);
                            _pendingCard = null;
                        }
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
            App.Log("Task.Run DB/OCR start");
            OracleTextProvider.Initialize();
            App.Log("OracleTextProvider done");
            _ocrEngine.Initialize();
            App.Log("OcrEngine.Initialize done");
            try
            {
                await _cardDatabase.LoadDatabaseAsync();
                App.Log($"CardDatabase.LoadDatabaseAsync done ({_cardDatabase.Count} cards)");
            }
            catch (Exception ex)
            {
                App.Log($"Card DB load failed: {ex.Message}");
            }
        });

        // 3. Start dedicated background tracking loop (off UI thread)
        App.Log("Calling StartBackgroundTracker");
        StartBackgroundTracker();

        // 4. Open Settings Window on startup so user immediately sees the control center
        App.Log("Calling OpenSettingsWindow");
        OpenSettingsWindow();
        App.Log("Window_Loaded complete");
    }

    private void StartBackgroundTracker()
    {
        _trackerCts?.Cancel();
        _trackerCts = new CancellationTokenSource();
        var token = _trackerCts.Token;

        _ = Task.Run(async () =>
        {
            App.Log("Background tracker thread running");
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
                        App.Log("DoBackgroundTrackStepAsync starting");
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

    public void ApplyStyleSettings()
    {
        if (OverlayWebView.CoreWebView2 == null) return;
        var msg = JsonSerializer.Serialize(new
        {
            action = "settings",
            settings = new
            {
                bgColor = _config.BgColor,
                bgOpacity = _config.Opacity,
                nameZhColor = _config.NameZhColor,
                nameZhSize = _config.NameZhSize,
                nameEnColor = _config.NameEnColor,
                nameEnSize = _config.NameEnSize,
                typeColor = _config.TypeColor,
                typeSize = _config.TypeSize,
                rulesColor = _config.RulesColor,
                rulesSize = _config.RulesSize,
                showEnRules = _config.ShowEnRules,
                enRulesColor = _config.EnRulesColor,
                enRulesSize = _config.EnRulesSize,
                ptColor = _config.PtColor,
                ptSize = _config.PtSize
            }
        });
        OverlayWebView.CoreWebView2.PostWebMessageAsJson(msg);

        // If overlay is currently visible with an active card, ensure Oracle text is fetched
        // and re-post the card to WebView2 so DOM structure is completely up-to-date
        if (_isOverlayVisible && _currentCard != null)
        {
            if (string.IsNullOrEmpty(_currentCard.OracleText) && !string.IsNullOrEmpty(_currentCard.NameEn))
            {
                var card = _currentCard;
                var ot = OracleTextProvider.GetOracleText(card.NameEn, (name, fetchedText) =>
                {
                    if (_currentCard?.NameEn == name)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            card.OracleText = fetchedText;
                            if (card.Faces != null && card.Faces.Count > 1 && fetchedText.Contains("//"))
                            {
                                var faceOracles = fetchedText.Split(new[] { "\n//\n", "//" }, StringSplitOptions.RemoveEmptyEntries);
                                for (int i = 0; i < card.Faces.Count && i < faceOracles.Length; i++)
                                {
                                    card.Faces[i].OracleText = faceOracles[i].Trim();
                                }
                            }
                            var p = JsonSerializer.Serialize(new
                            {
                                action = "showCard",
                                card = card
                            });
                            OverlayWebView.CoreWebView2?.PostWebMessageAsJson(p);
                        }));
                    }
                });
                if (!string.IsNullOrEmpty(ot))
                {
                    card.OracleText = ot;
                    if (card.Faces != null && card.Faces.Count > 1 && ot.Contains("//"))
                    {
                        var faceOracles = ot.Split(new[] { "\n//\n", "//" }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < card.Faces.Count && i < faceOracles.Length; i++)
                        {
                            card.Faces[i].OracleText = faceOracles[i].Trim();
                        }
                    }
                }
            }

            var cardMsg = JsonSerializer.Serialize(new
            {
                action = "showCard",
                card = _currentCard
            });
            OverlayWebView.CoreWebView2.PostWebMessageAsJson(cardMsg);
        }
    }

    private void HandleTriggerModeChanged(TriggerMode mode)
    {
        _config.Mode = TriggerMode.PreviewPane;
        _config.Save();
        _captureBoxWindow?.Show();
    }

    private async Task DoBackgroundTrackStepAsync()
    {
        await HandleTrackingBackgroundAsync();
    }

    private async Task HandleTrackingBackgroundAsync()
    {
        // 1. Check if Move mode or CaptureBox resizing is active
        if (_isMoveModeActive || (_captureBoxWindow != null && _captureBoxWindow.IsResizing))
        {
            return;
        }

        // 2. Foreground check & Z-order / Topmost strategy for CaptureBox and Overlay
        IntPtr fg = Win32.GetForegroundWindow();
        bool isMtgoOrOur = fg != IntPtr.Zero && IsMtgoOrOurWindow(fg);

        if (!isMtgoOrOur)
        {
            // Focus has switched to another application (browser, chat, desktop, etc.)
            // Demote capture box and immediately dismiss/demote overlay so it does NOT stay in front of other apps
            _captureBoxWindow?.DemoteFromTopmost();

            if (_isOverlayVisible)
            {
                _isOverlayVisible = false;
                _currentCardName = null;
                _currentCard = null;
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    Topmost = false;
                    if (_myHwnd != IntPtr.Zero)
                    {
                        Win32.SetWindowPos(_myHwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
                            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                    }
                    HideOverlay();
                }));
            }
            return;
        }

        // MTGO is active foreground
        _captureBoxWindow?.EnsureTopmost();

        // 3. Mouse Click Check & Cooldown:
        // Dismiss overlay immediately on click so it never blocks clicks or flashes under the mouse.
        bool isMouseDown = (Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0 ||
                           (Win32.GetAsyncKeyState(Win32.VK_RBUTTON) & 0x8000) != 0 ||
                           (Win32.GetAsyncKeyState(Win32.VK_MBUTTON) & 0x8000) != 0;

        if (isMouseDown)
        {
            _lastClickTime = DateTime.UtcNow;
            if (_isOverlayVisible)
            {
                _isOverlayVisible = false;
                _currentCardName = null;
                _currentCard = null;
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    Topmost = false;
                    if (_myHwnd != IntPtr.Zero)
                    {
                        Win32.SetWindowPos(_myHwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
                            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                    }
                    HideOverlay();
                }));
            }
            return;
        }

        // Suppress triggering new overlay popups during and immediately after a mouse click (300ms cooldown)
        if ((DateTime.UtcNow - _lastClickTime).TotalMilliseconds < 300)
        {
            return;
        }

        // 4. CTRL Modifier Check
        bool isCtrlDown = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
        if (_config.RequireCtrlKey && !isCtrlDown)
        {
            if (_currentCardName != null || _isOverlayVisible)
            {
                _isOverlayVisible = false;
                _lastPreviewHash = 0; // When requiring Ctrl, releasing Ctrl allows re-triggering upon next press
                await Dispatcher.BeginInvoke(new Action(HideOverlay));
            }
            return;
        }

        // 5. Cursor position & screen bounds
        Win32.GetCursorPos(out var pt);
        var cursorPos = new System.Drawing.Point(pt.X, pt.Y);
        Rectangle previewRect = _captureBoxWindow?.CachedPhysicalCaptureRect ?? _config.GetCaptureBoxRect();

        if (previewRect.IsEmpty || previewRect.Width < 20 || previewRect.Height < 10)
        {
            return;
        }

        using var titleBmp = ScreenCapture.CapturePreviewTitle(previewRect);
        if (titleBmp == null) return;

        ulong currentHash = ScreenCapture.ComputeFastSampleHash(titleBmp);

        // 6. Stale-image / unchanged preview check:
        // Rule 1: The overlay does NOT continuously follow the mouse. It stays parked where triggered.
        // Rule 2: If the overlay was dismissed (e.g. by mouse click), it remains hidden until a NEW card appears.
        if (currentHash == _lastPreviewHash)
        {
            return;
        }

        // 7. Preview pane has new content - update hash and run OCR recognition
        _lastPreviewHash = currentHash;
        _lastCursorPos = cursorPos;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (rawOcr, card) = await _ocrEngine.RecognizeAndLookupAsync(titleBmp, text => _cardDatabase.Lookup(text));
        sw.Stop();

        App.Log($"[PreviewPane OCR] '{rawOcr}' -> Card: '{(card != null ? card.NameEn : "null")}' in {sw.ElapsedMilliseconds}ms");
        _captureBoxWindow?.UpdateOcrFeedback(rawOcr, card, sw.ElapsedMilliseconds);
        _settingsWindow?.UpdateDiagnostic(titleBmp, rawOcr, card, sw.ElapsedMilliseconds, "🔲 手动识别框锚定 (Card Preview)");

        if (card != null)
        {
            _nullCardCount = 0;
            _currentCardName = card.NameEn;
            _currentCard = card;
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                PositionAndShow(card, cursorPos, previewRect, null);
            }));
        }
        else
        {
            // If card is null (empty preview pane, token, or non-card game UI)
            _nullCardCount++;
            if (_nullCardCount >= 2) // Debounce 2 frames (~160ms) to avoid flicker on brief OCR noise
            {
                if (_isOverlayVisible || _currentCardName != null)
                {
                    _currentCardName = null;
                    _currentCard = null;
                    await Dispatcher.BeginInvoke(new Action(HideOverlay));
                }
                _nullCardCount = 0;
            }
        }
    }

    private static bool IsMtgoOrOurWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        try
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return true;
            using var proc = Process.GetProcessById((int)pid);
            if (proc.ProcessName.IndexOf("MTGO", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            var sb = new System.Text.StringBuilder(256);
            Win32.GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Magic: The Gathering", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureWithinVisibleBounds(ref double left, ref double top, double width, double height)
    {
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vWidth = SystemParameters.VirtualScreenWidth;
        double vHeight = SystemParameters.VirtualScreenHeight;

        // If outside or partly off visible monitors, bring within bounds
        if (left < vLeft || left + width > vLeft + vWidth ||
            top < vTop || top + height > vTop + vHeight)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea 
                          ?? new Rectangle(0, 0, 1920, 1080);
            var dpi = VisualTreeHelper.GetDpi(this);
            left = (primary.Left + primary.Width - 420) / dpi.DpiScaleX;
            top = (primary.Top + 80) / dpi.DpiScaleY;
        }
    }

    private void PositionAndShow(CardEntry card, System.Drawing.Point cursorPos, Rectangle? previewRect, Rectangle? detectedZoomBox = null)
    {
        _currentCard = card;
        if (!string.IsNullOrEmpty(card.NameEn) && string.IsNullOrEmpty(card.OracleText))
        {
            var ot = OracleTextProvider.GetOracleText(card.NameEn, (name, fetchedText) =>
            {
                if (_currentCardName == name && _currentCard != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _currentCard.OracleText = fetchedText;
                        if (_currentCard.Faces != null && _currentCard.Faces.Count > 1 && fetchedText.Contains("//"))
                        {
                            var faceOracles = fetchedText.Split(new[] { "\n//\n", "//" }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < _currentCard.Faces.Count && i < faceOracles.Length; i++)
                            {
                                _currentCard.Faces[i].OracleText = faceOracles[i].Trim();
                            }
                        }

                        if (_isOverlayVisible)
                        {
                            var p = JsonSerializer.Serialize(new
                            {
                                action = "showCard",
                                card = _currentCard
                            });
                            OverlayWebView.CoreWebView2?.PostWebMessageAsJson(p);
                        }
                    }));
                }
            });
            if (!string.IsNullOrEmpty(ot))
            {
                card.OracleText = ot;
                if (card.Faces != null && card.Faces.Count > 1 && ot.Contains("//"))
                {
                    var faceOracles = ot.Split(new[] { "\n//\n", "//" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < card.Faces.Count && i < faceOracles.Length; i++)
                    {
                        card.Faces[i].OracleText = faceOracles[i].Trim();
                    }
                }
            }
        }

        double targetLeft;
        double targetTop;

        switch (_config.PositionMode)
        {
            case OverlayPositionMode.CustomFixed:
                targetLeft = _config.FixedLeft;
                targetTop = _config.FixedTop;
                EnsureWithinVisibleBounds(ref targetLeft, ref targetTop, Width, Height);
                break;

            case OverlayPositionMode.BesidePreview when previewRect.HasValue && !detectedZoomBox.HasValue:
                CalculateBesidePreviewPosition(previewRect.Value, out targetLeft, out targetTop);
                break;

            case OverlayPositionMode.FollowCursor:
            default:
                CalculateSmartCursorPosition(cursorPos, previewRect, detectedZoomBox, out targetLeft, out targetTop);
                break;
        }

        Left = targetLeft;
        Top = targetTop;
        _isOverlayVisible = true;
        Visibility = Visibility.Visible;
        Topmost = true;

        if (_myHwnd != IntPtr.Zero)
        {
            Win32.ExcludeWindowAndChildrenFromCapture(_myHwnd);
            Win32.SetWindowPos(_myHwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOACTIVATE);
        }

        var payload = JsonSerializer.Serialize(new
        {
            action = "showCard",
            card = card
        });

        OverlayWebView.CoreWebView2?.PostWebMessageAsJson(payload);
    }

    private void CalculateSmartCursorPosition(System.Drawing.Point cursorPos, Rectangle? previewRect, out double left, out double top)
    {
        CalculateSmartCursorPosition(cursorPos, previewRect, null, out left, out top);
    }

    private void CalculateSmartCursorPosition(
        System.Drawing.Point cursorPos,
        Rectangle? previewRect,
        Rectangle? detectedZoomBox,
        out double left,
        out double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double curX = cursorPos.X / dpi.DpiScaleX;
        double curY = cursorPos.Y / dpi.DpiScaleY;

        var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
        double scrLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        double scrRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        double scrTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        double scrBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

        double overlayWidth = Width;
        double overlayHeight = Height;
        double zoomGap = 16;

        if (_config.AvoidCardZoom)
        {
            Rectangle zoomBox = Rectangle.Empty;
            bool isDetected = false;
            bool isRightOfCursor = true;

            if (detectedZoomBox.HasValue && !detectedZoomBox.Value.IsEmpty)
            {
                zoomBox = detectedZoomBox.Value;
                isDetected = true;
                isRightOfCursor = zoomBox.Left >= cursorPos.X - 30;
            }
            else
            {
                var zoomDet = CardZoomDetector.DetectFromScreen(cursorPos, screen.Bounds, previewRect);
                if (zoomDet.IsDetected)
                {
                    zoomBox = zoomDet.BoundingBox;
                    isDetected = true;
                    isRightOfCursor = zoomDet.IsRightOfCursor;
                }
            }

            if (isDetected)
            {
                double detectedZoomLeft = zoomBox.Left / dpi.DpiScaleX;
                double detectedZoomRight = zoomBox.Right / dpi.DpiScaleX;
                double detectedZoomTop = zoomBox.Top / dpi.DpiScaleY;

                if (isRightOfCursor)
                {
                    // Detected zoom is on the RIGHT of cursor:
                    // Place overlay on the LEFT of cursor/card
                    double candLeft = curX - zoomGap - overlayWidth;
                    if (candLeft >= scrLeft + 15)
                    {
                        left = candLeft;
                    }
                    else
                    {
                        // Left edge constrained: place to the RIGHT of detected zoom card
                        left = detectedZoomRight + zoomGap;
                    }
                }
                else
                {
                    // Detected zoom is on the LEFT of cursor (left-flipped zoom):
                    // Place overlay to the RIGHT of cursor / zoom card
                    double candRight = curX + 24;
                    if (candRight + overlayWidth <= scrRight - 15)
                    {
                        left = candRight;
                    }
                    else
                    {
                        // Right edge constrained: place to the LEFT of detected zoom card
                        left = detectedZoomLeft - zoomGap - overlayWidth;
                    }
                }

                top = Math.Max(scrTop + 10, detectedZoomTop);
                if (top + overlayHeight > scrBottom - 15)
                {
                    top = Math.Max(scrTop + 10, scrBottom - overlayHeight - 15);
                }

                ClampToScreen(ref left, ref top, overlayWidth, overlayHeight, scrLeft, scrRight, scrTop, scrBottom);
                AvoidPreviewPane(ref left, ref top, overlayWidth, overlayHeight, previewRect, dpi, scrLeft, scrRight);
                return;
            }

            // 2. Predictive model before MTGO's hover delay triggers (~300ms):
            // Place overlay on the LEFT of cursor (Zero-overlap with typical right-expanding zoom)
            double candLeftPred = curX - zoomGap - overlayWidth;
            double candRightPred = curX + 24 + Math.Max(260, _config.CardZoomWidth) + zoomGap;

            if (candLeftPred >= scrLeft + 15)
            {
                left = candLeftPred;
            }
            else if (candRightPred + overlayWidth <= scrRight - 15)
            {
                left = candRightPred;
            }
            else
            {
                left = Math.Max(scrLeft + 10, scrRight - overlayWidth - 10);
            }
        }
        else
        {
            // Classic cursor follow without zoom avoidance
            double offsetH = 26;
            if (curX + offsetH + overlayWidth <= scrRight - 15)
            {
                left = curX + offsetH;
            }
            else
            {
                left = curX - overlayWidth - offsetH;
            }

            if (left < scrLeft + 10) left = scrLeft + 10;
            if (left + overlayWidth > scrRight - 10) left = scrRight - overlayWidth - 10;
        }

        top = curY - 36;
        if (top + overlayHeight > scrBottom - 15)
        {
            top = Math.Max(scrTop + 10, scrBottom - overlayHeight - 15);
        }
        if (top < scrTop + 10) top = scrTop + 10;

        AvoidPreviewPane(ref left, ref top, overlayWidth, overlayHeight, previewRect, dpi, scrLeft, scrRight);
    }

    private void CheckAndAdjustForCardZoom(Rectangle? previewRect)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isOverlayVisible || _config.PositionMode != OverlayPositionMode.FollowCursor || !_config.AvoidCardZoom)
            {
                return;
            }

            if (!Win32.GetCursorPos(out var pt)) return;
            var cursorPos = new System.Drawing.Point(pt.X, pt.Y);
            var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
            var dpi = VisualTreeHelper.GetDpi(this);

            var zoomDet = CardZoomDetector.DetectFromScreen(cursorPos, screen.Bounds, previewRect);
            if (!zoomDet.IsDetected) return;

            double detectedZoomLeft = zoomDet.BoundingBox.Left / dpi.DpiScaleX;
            double detectedZoomRight = zoomDet.BoundingBox.Right / dpi.DpiScaleX;
            double detectedZoomTop = zoomDet.BoundingBox.Top / dpi.DpiScaleY;
            double detectedZoomBottom = zoomDet.BoundingBox.Bottom / dpi.DpiScaleY;

            // Check if current overlay rect overlaps with the detected zoom card
            double curOverlayLeft = Left;
            double curOverlayRight = Left + Width;
            double curOverlayTop = Top;
            double curOverlayBottom = Top + Height;

            bool overlaps = (curOverlayLeft < detectedZoomRight + 8 &&
                             curOverlayRight > detectedZoomLeft - 8 &&
                             curOverlayTop < detectedZoomBottom + 8 &&
                             curOverlayBottom > detectedZoomTop - 8);

            if (overlaps)
            {
                CalculateSmartCursorPosition(cursorPos, previewRect, out double newLeft, out double newTop);
                if (Math.Abs(newLeft - Left) > 4 || Math.Abs(newTop - Top) > 4)
                {
                    Left = newLeft;
                    Top = newTop;
                }
            }
        }));
    }

    private static void ClampToScreen(ref double left, ref double top, double w, double h, double scrLeft, double scrRight, double scrTop, double scrBottom)
    {
        if (left < scrLeft + 10) left = scrLeft + 10;
        if (left + w > scrRight - 10) left = scrRight - w - 10;
        if (top < scrTop + 10) top = scrTop + 10;
        if (top + h > scrBottom - 15) top = Math.Max(scrTop + 10, scrBottom - h - 15);
    }

    private static void AvoidPreviewPane(ref double left, ref double top, double overlayWidth, double overlayHeight, Rectangle? previewRect, DpiScale dpi, double scrLeft, double scrRight)
    {
        if (!previewRect.HasValue || previewRect.Value.IsEmpty) return;

        double prevL = previewRect.Value.Left / dpi.DpiScaleX;
        double prevR = previewRect.Value.Right / dpi.DpiScaleX;
        double prevT = previewRect.Value.Top / dpi.DpiScaleY;
        double prevB = previewRect.Value.Bottom / dpi.DpiScaleY;

        // Only avoid if preview rect is on the same monitor screen bounds
        if (prevR < scrLeft || prevL > scrRight) return;

        if (left < prevR + 8 && left + overlayWidth > prevL - 8 &&
            top < prevB + 8 && top + overlayHeight > prevT - 8)
        {
            if (prevR + 12 + overlayWidth <= scrRight - 15)
            {
                left = prevR + 12;
            }
            else if (prevL - 12 - overlayWidth >= scrLeft + 15)
            {
                left = prevL - 12 - overlayWidth;
            }
        }
    }

    private void CalculateBesidePreviewPosition(Rectangle previewRect, out double left, out double top)
    {
        var screen = System.Windows.Forms.Screen.FromRectangle(previewRect);
        var dpi = VisualTreeHelper.GetDpi(this);

        double scrLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        double scrRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        double scrTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        double scrBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

        double prevRight = previewRect.Right / dpi.DpiScaleX;
        double prevLeft = previewRect.Left / dpi.DpiScaleX;
        double prevTop = previewRect.Top / dpi.DpiScaleY;

        if (prevRight + 12 + Width <= scrRight - 15)
        {
            left = prevRight + 12;
        }
        else if (prevLeft - 12 - Width >= scrLeft + 15)
        {
            left = prevLeft - 12 - Width;
        }
        else
        {
            left = Math.Max(scrLeft + 10, scrRight - Width - 15);
        }

        top = Math.Max(scrTop + 10, prevTop);
        if (top + Height > scrBottom - 15)
        {
            top = Math.Max(scrTop + 10, scrBottom - Height - 15);
        }
    }

    private void HideOverlay()
    {
        if (_isMoveModeActive) return;

        _currentCardName = null;
        _currentCard = null;
        _isOverlayVisible = false;
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

        double curLeft = Left;
        double curTop = Top;
        EnsureWithinVisibleBounds(ref curLeft, ref curTop, Width, Height + 46);
        Left = curLeft;
        Top = curTop;

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: false);

        MoveBar.Visibility = Visibility.Visible;
        Height = _config.Height + 46;
        Topmost = true;
        if (hWnd != IntPtr.Zero)
        {
            Win32.SetWindowPos(hWnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW);
        }
        Activate();
        Focus();

        _trayIcon?.ShowBalloon("调整位置模式", "请按住顶部的金色横条拖拽浮窗至理想位置，随后点击「保存并锁定」或「取消」。");
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
        Height = _config.Height;

        _config.FixedLeft = Left;
        _config.FixedTop = Top;
        _config.PositionMode = OverlayPositionMode.CustomFixed;
        _config.Save();

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: true);

        HideOverlay();

        _settingsWindow?.RefreshPositionMode(OverlayPositionMode.CustomFixed);
        _trayIcon?.RefreshPositionUI(OverlayPositionMode.CustomFixed);
        _trayIcon?.ShowBalloon("位置已保存并锁定", $"浮窗位置已锁定至坐标 ({Math.Round(Left)}, {Math.Round(Top)})。随时可通过设置面板或托盘菜单恢复鼠标跟随。");
    }

    private void CancelMovePosition_Click(object sender, RoutedEventArgs e)
    {
        _isMoveModeActive = false;
        MoveBar.Visibility = Visibility.Collapsed;
        Height = _config.Height;

        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: true);
        HideOverlay();
    }

    #endregion

    private void OpenSettingsWindow()
    {
        _settingsWindow?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _settingsWindow.UpdateCaptureBoxStatusText();
            _settingsWindow.Show();
            _settingsWindow.IsSettingsVisible = true;
            _settingsWindow.Activate();
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }
        }));
    }

    private async Task<(Bitmap? bmp, string ocr, CardEntry? card, long elapsed)> RunTestOcrCaptureAsync()
    {
        Win32.GetCursorPos(out var pt);
        var curPt = new System.Drawing.Point(pt.X, pt.Y);
        var screen = System.Windows.Forms.Screen.FromPoint(curPt);
        Rectangle previewRect = Rectangle.Empty;

        if (_captureBoxWindow != null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                previewRect = _captureBoxWindow.GetPhysicalCaptureRect();
            });
        }

        if (previewRect.IsEmpty || previewRect.Width < 20 || previewRect.Height < 10)
        {
            previewRect = _config.GetCaptureBoxRect();
        }

        var titleBmp = ScreenCapture.CapturePreviewTitle(previewRect);
        if (titleBmp == null) return (null, "截屏失败 (区域为空或超出屏幕)", null, 0);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (rawOcr, card) = await _ocrEngine.RecognizeAndLookupAsync(titleBmp, text => _cardDatabase.Lookup(text));
        sw.Stop();

        return (titleBmp, rawOcr, card, sw.ElapsedMilliseconds);
    }

    private void OpenCalibration()
    {
        var calibWindow = new CalibrationWindow(_windowTracker, _config, rect =>
        {
            _lastPreviewHash = 0;
            _captureBoxWindow?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _captureBoxWindow.SetPhysicalCaptureRect(rect);
                _captureBoxWindow.Visibility = Visibility.Visible;
            }));
            _settingsWindow?.UpdateCaptureBoxStatusText();
            _trayIcon?.ShowBalloon("校准完成", "卡名横条校准成功！已更新识别框坐标。");
        });
        calibWindow.ShowDialog();
    }

    private void OpenCaptureBoxAdjustMode()
    {
        _captureBoxWindow?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _captureBoxWindow.Visibility = Visibility.Visible;
            _captureBoxWindow.SetLocked(false);
            _captureBoxWindow.EnsureTopmost();
            _captureBoxWindow.Activate();
        }));

        _trayIcon?.ShowBalloon("调整卡牌识别框", "已开启识别框调整模式。请直接拖拽或拉伸悬浮框以精准贴合英文卡名，调整完毕后点击「锁定穿透」。");
    }

    private void ToggleCaptureBoxLock(bool locked)
    {
        _captureBoxWindow?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _captureBoxWindow.SetLocked(locked);
        }));
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
            OracleText = "Lightning Bolt deals 3 damage to any target.",
            FlavorText = "「太棒了，这真不可思议！不过我可不想再经历第二次了。」——探险家贾米拉",
            Source = "本地字典 (测试)"
        };

        _currentCard = sampleCard;
        _currentCardName = sampleCard.NameEn;
        _isOverlayVisible = true;

        if (_config.PositionMode == OverlayPositionMode.CustomFixed)
        {
            double fixedL = _config.FixedLeft;
            double fixedT = _config.FixedTop;
            EnsureWithinVisibleBounds(ref fixedL, ref fixedT, Width, Height);
            Left = fixedL;
            Top = fixedT;
        }
        else
        {
            Win32.GetCursorPos(out var pt);
            CalculateSmartCursorPosition(new System.Drawing.Point(pt.X, pt.Y), null, out double l, out double t);
            Left = l;
            Top = t;
        }

        Visibility = Visibility.Visible;
        Topmost = true;
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        if (hWnd != IntPtr.Zero)
        {
            Win32.SetWindowPos(hWnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOACTIVATE);
        }

        var payload = JsonSerializer.Serialize(new
        {
            action = "showCard",
            card = sampleCard
        });

        OverlayWebView.CoreWebView2?.PostWebMessageAsJson(payload);
    }

    private static string GetModeDisplayName(TriggerMode mode) => mode switch
    {
        TriggerMode.Dual => "⚡ 双引擎智能融合",
        TriggerMode.Hover => "🌟 智能悬浮大图识别",
        TriggerMode.PreviewPane => "🔲 手动识别框锚定",
        TriggerMode.HoldCtrl => "按住 Ctrl 键取词",
        _ => "智能悬浮大图识别"
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
        _ocrEngine.Dispose();
        base.OnClosed(e);
    }
}
