using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MTGOOverlay.Models;
using MTGOOverlay.Native;

namespace MTGOOverlay;

public partial class CalibrationWindow : Window
{
    private readonly WindowTracker _windowTracker;
    private readonly AppConfig _config;
    private readonly Action<Rectangle> _onCalibrated;

    private bool _isDragging;
    private System.Windows.Point _startPoint;
    private Rectangle _mtgoWindowRect;

    public CalibrationWindow(WindowTracker windowTracker, AppConfig config, Action<Rectangle> onCalibrated)
    {
        InitializeComponent();
        _windowTracker = windowTracker;
        _config = config;
        _onCalibrated = onCalibrated;

        // Position across entire virtual screen
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        IntPtr hWnd = _windowTracker.GetMtgoWindowHandle();
        if (hWnd != IntPtr.Zero && Win32.GetWindowRect(hWnd, out var rect))
        {
            _mtgoWindowRect = rect.ToRectangle();
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            Close();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);

            Canvas.SetLeft(SelectionBox, _startPoint.X);
            Canvas.SetTop(SelectionBox, _startPoint.Y);
            SelectionBox.Width = 0;
            SelectionBox.Height = 0;
            SelectionBox.Visibility = Visibility.Visible;
        }
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetPosition(this);
        double left = Math.Min(_startPoint.X, currentPoint.X);
        double top = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _startPoint.X);
        double height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionBox, left);
        Canvas.SetTop(SelectionBox, top);
        SelectionBox.Width = width;
        SelectionBox.Height = height;
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        var endPoint = e.GetPosition(this);
        double dipLeft = Math.Min(_startPoint.X, endPoint.X);
        double dipTop = Math.Min(_startPoint.Y, endPoint.Y);
        double dipWidth = Math.Abs(endPoint.X - _startPoint.X);
        double dipHeight = Math.Abs(endPoint.Y - _startPoint.Y);

        if (dipWidth < 40 || dipHeight < 15)
        {
            // Too small, ignore
            SelectionBox.Visibility = Visibility.Collapsed;
            return;
        }

        // Convert DIPs to physical pixels
        var dpi = VisualTreeHelper.GetDpi(this);
        int physLeft = (int)((Left + dipLeft) * dpi.DpiScaleX);
        int physTop = (int)((Top + dipTop) * dpi.DpiScaleY);
        int physWidth = (int)(dipWidth * dpi.DpiScaleX);
        int physHeight = (int)(dipHeight * dpi.DpiScaleY);

        var calibratedRect = new Rectangle(physLeft, physTop, physWidth, physHeight);

        _config.CaptureBoxX = calibratedRect.Left;
        _config.CaptureBoxY = calibratedRect.Top;
        _config.CaptureBoxWidth = calibratedRect.Width;
        _config.CaptureBoxHeight = calibratedRect.Height;
        _config.Mode = TriggerMode.PreviewPane;
        _config.Save();

        _onCalibrated?.Invoke(calibratedRect);
        Close();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
