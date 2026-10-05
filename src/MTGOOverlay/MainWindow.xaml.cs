using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
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

    private System.Drawing.Point _lastMousePos;
    private DateTime _mouseStationaryStart = DateTime.MinValue;
    private string? _currentCardName;
    private bool _isScanning;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        // Make click-through and non-activating
        Win32.SetOverlayWindowStyles(hWnd, clickThrough: true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Initialize WebView2
        string webFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        if (!Directory.Exists(webFolder))
        {
            webFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "web"));
        }

        await OverlayWebView.EnsureCoreWebView2Async();
        string indexPath = Path.Combine(webFolder, "index.html");
        if (File.Exists(indexPath))
        {
            OverlayWebView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
        }

        // 2. Initialize OCR & Card Database in background
        _ocrEngine.Initialize();
        await Task.Run(async () =>
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

        // 3. Start mouse tracking timer
        _trackerTimer.Interval = TimeSpan.FromMilliseconds(80);
        _trackerTimer.Tick += OnTrackerTick;
        _trackerTimer.Start();
    }

    private async void OnTrackerTick(object? sender, EventArgs e)
    {
        if (_isScanning || !_cardDatabase.IsLoaded || !_ocrEngine.IsAvailable) return;

        if (!Win32.GetCursorPos(out var pt)) return;
        var currentPos = new System.Drawing.Point(pt.X, pt.Y);

        int deltaX = Math.Abs(currentPos.X - _lastMousePos.X);
        int deltaY = Math.Abs(currentPos.Y - _lastMousePos.Y);

        if (deltaX > 4 || deltaY > 4)
        {
            // Mouse moved
            _lastMousePos = currentPos;
            _mouseStationaryStart = DateTime.UtcNow;

            // If mouse moved far away from current card, hide overlay
            if (_currentCardName != null && (deltaX > 80 || deltaY > 80))
            {
                HideOverlay();
            }
            return;
        }

        // Mouse has been stationary
        var stationaryDuration = DateTime.UtcNow - _mouseStationaryStart;
        if (stationaryDuration.TotalMilliseconds < 160) return; // 160ms debounce

        // Check if cursor is inside MTGO
        if (!_windowTracker.IsCursorInsideMtgo(out var cursorPos, out var windowRect))
        {
            return;
        }

        _isScanning = true;
        try
        {
            await ScanCardUnderCursorAsync(cursorPos);
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task ScanCardUnderCursorAsync(System.Drawing.Point cursorPos)
    {
        // Capture around cursor (where card title typically resides upon hovering)
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
                ShowCard(card, cursorPos);
            }
        }
    }

    private void ShowCard(CardEntry card, System.Drawing.Point cursorPos)
    {
        // Position overlay near cursor with edge padding
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        double left = cursorPos.X + 24;
        if (left + Width > screenWidth - 10)
        {
            left = cursorPos.X - Width - 24;
        }

        double top = cursorPos.Y - 50;
        if (top + Height > screenHeight - 10)
        {
            top = screenHeight - Height - 10;
        }
        if (top < 10) top = 10;

        Left = left;
        Top = top;
        Visibility = Visibility.Visible;

        // Post message to WebView2
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
        Visibility = Visibility.Hidden;
        OverlayWebView.CoreWebView2?.PostWebMessageAsJson("{\"action\":\"clear\"}");
    }
}
