using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MTGOOverlay.Models;

namespace MTGOOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<TriggerMode>? _onTriggerModeChanged;
    private readonly Action<bool> _onCtrlModifierChanged;
    private readonly Action<OverlayPositionMode> _onPosModeChanged;
    private readonly Action _onUnlockCaptureBox;
    private readonly Action _onLockCaptureBox;
    private readonly Action<bool> _onToggleBorder;
    private readonly Action _onCalibrateFull;
    private readonly Action _onMoveOverlay;
    private readonly Action _onStyleChanged;
    private readonly Func<Task<(Bitmap? bmp, string ocr, CardEntry? card, long elapsed)>>? _onTestCaptureRequested;

    public volatile bool IsSettingsVisible = false;
    private bool _isUpdatingUi;

    public SettingsWindow(
        AppConfig config,
        Action<bool> onCtrlModifierChanged,
        Action<OverlayPositionMode> onPosModeChanged,
        Action onUnlockCaptureBox,
        Action onLockCaptureBox,
        Action<bool> onToggleBorder,
        Action onCalibrateFull,
        Action onMoveOverlay,
        Action onStyleChanged,
        Func<Task<(Bitmap? bmp, string ocr, CardEntry? card, long elapsed)>>? onTestCaptureRequested = null,
        Action<TriggerMode>? onTriggerModeChanged = null)
    {
        _isUpdatingUi = true;
        _config = config;
        _onTriggerModeChanged = onTriggerModeChanged;
        _onCtrlModifierChanged = onCtrlModifierChanged;
        _onPosModeChanged = onPosModeChanged;
        _onUnlockCaptureBox = onUnlockCaptureBox;
        _onLockCaptureBox = onLockCaptureBox;
        _onToggleBorder = onToggleBorder;
        _onCalibrateFull = onCalibrateFull;
        _onMoveOverlay = onMoveOverlay;
        _onStyleChanged = onStyleChanged;
        _onTestCaptureRequested = onTestCaptureRequested;

        InitializeComponent();
        IsVisibleChanged += (s, e) => { IsSettingsVisible = IsVisible; };
        LoadConfigIntoUi();
    }

    private void LoadConfigIntoUi()
    {
        _isUpdatingUi = true;
        try
        {
            // Trigger mode
            ChkRequireCtrl.IsChecked = _config.RequireCtrlKey;

            // Capture Box
            UpdateCaptureBoxStatusText();
            ChkShowBorder.IsChecked = _config.ShowCaptureBoxBorder;

            // Position mode
            RbPosFollowCursor.IsChecked = _config.PositionMode == OverlayPositionMode.FollowCursor;
            ChkAvoidCardZoom.IsChecked = _config.AvoidCardZoom;
            RbPosBesidePreview.IsChecked = _config.PositionMode == OverlayPositionMode.BesidePreview;
            RbPosCustomFixed.IsChecked = _config.PositionMode == OverlayPositionMode.CustomFixed;

            // 1. Background & Opacity
            SelectComboByTag(CmbBgColor, _config.BgColor);
            SliderOpacity.Value = _config.Opacity;
            TxtOpacityValue.Text = $"{Math.Round(_config.Opacity * 100)}%";

            // 2. Card Name (Zh & En)
            SliderNameZhSize.Value = _config.NameZhSize;
            TxtNameZhSizeValue.Text = $"{_config.NameZhSize} px";
            SelectComboByTag(CmbNameZhColor, _config.NameZhColor);

            SliderNameEnSize.Value = _config.NameEnSize;
            TxtNameEnSizeValue.Text = $"{_config.NameEnSize} px";
            SelectComboByTag(CmbNameEnColor, _config.NameEnColor);

            // 3. Type Line
            SliderTypeSize.Value = _config.TypeSize;
            TxtTypeSizeValue.Text = $"{_config.TypeSize} px";
            SelectComboByTag(CmbTypeColor, _config.TypeColor);

            // 4. Rules Text & English Rules
            SliderRulesSize.Value = _config.RulesSize;
            TxtRulesSizeValue.Text = $"{_config.RulesSize} px";
            SelectComboByTag(CmbRulesColor, _config.RulesColor);

            ChkShowEnRules.IsChecked = _config.ShowEnRules;
            PanelEnRulesOptions.Visibility = _config.ShowEnRules ? Visibility.Visible : Visibility.Collapsed;

            SliderEnRulesSize.Value = _config.EnRulesSize;
            TxtEnRulesSizeValue.Text = $"{_config.EnRulesSize} px";
            SelectComboByTag(CmbEnRulesColor, _config.EnRulesColor);

            // 5. P/T & Loyalty
            SliderPtSize.Value = _config.PtSize;
            TxtPtSizeValue.Text = $"{_config.PtSize} px";
            SelectComboByTag(CmbPtColor, _config.PtColor);
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private static void SelectComboByTag(ComboBox cmb, string? tagValue)
    {
        if (cmb == null || tagValue == null) return;
        for (int i = 0; i < cmb.Items.Count; i++)
        {
            if (cmb.Items[i] is ComboBoxItem cbi && 
                string.Equals(cbi.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedIndex = i;
                return;
            }
        }
        if (cmb.SelectedIndex < 0 && cmb.Items.Count > 0) cmb.SelectedIndex = 0;
    }

    public void UpdateCaptureBoxStatusText()
    {
        TxtCaptureBoxStatus.Text = $"当前尺寸: {_config.CaptureBoxWidth} × {_config.CaptureBoxHeight} px | 桌面坐标: ({_config.CaptureBoxX}, {_config.CaptureBoxY})";
    }

    public void RefreshPositionMode(OverlayPositionMode posMode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshPositionMode(posMode)));
            return;
        }

        _isUpdatingUi = true;
        try
        {
            RbPosFollowCursor.IsChecked = posMode == OverlayPositionMode.FollowCursor;
            RbPosBesidePreview.IsChecked = posMode == OverlayPositionMode.BesidePreview;
            RbPosCustomFixed.IsChecked = posMode == OverlayPositionMode.CustomFixed;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    public void RefreshCtrlModifier(bool require)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshCtrlModifier(require)));
            return;
        }

        _isUpdatingUi = true;
        try
        {
            ChkRequireCtrl.IsChecked = require;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    public void RefreshTriggerMode(TriggerMode mode)
    {
    }

    private static BitmapImage? BitmapToBitmapImageSafe(Bitmap? bmp)
    {
        if (bmp == null) return null;
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    public void UpdateDiagnostic(Bitmap? bmp, string rawOcr, CardEntry? card, long elapsedMs, string? sourceDesc = null)
    {
        if (!IsSettingsVisible) return;

        BitmapImage? frozenImage = BitmapToBitmapImageSafe(bmp);
        string resText = bmp != null ? $"{bmp.Width} × {bmp.Height} px" : "--";
        string badgeText = sourceDesc ?? (bmp != null ? (bmp.Height > 150 ? "🌟 智能悬浮大图 (全卡大图范围)" : "🔲 手动识别框 (固定卡名横条)") : "--");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateDiagnosticInternal(frozenImage, rawOcr, card, elapsedMs, badgeText, resText)));
            return;
        }

        UpdateDiagnosticInternal(frozenImage, rawOcr, card, elapsedMs, badgeText, resText);
    }

    private void UpdateDiagnosticInternal(BitmapImage? image, string rawOcr, CardEntry? card, long elapsedMs, string badgeText, string resText)
    {
        if (!IsSettingsVisible) return;

        if (image != null)
        {
            ImgCropPreview.Source = image;
        }

        TxtImageSourceBadge.Text = $"来源: {badgeText}";
        TxtImageResolution.Text = $"分辨率: {resText}";

        TxtDiagnosticOcr.Text = string.IsNullOrWhiteSpace(rawOcr) ? "(无文字识别结果)" : rawOcr.Replace("\r", " ").Replace("\n", " ").Trim();

        if (card != null)
        {
            TxtDiagnosticCard.Foreground = System.Windows.Media.Brushes.LightGreen;
            TxtDiagnosticCard.Text = $"⚡ {card.NameZh} ({card.NameEn}) [{card.TypeLine}]";
        }
        else if (!string.IsNullOrWhiteSpace(rawOcr))
        {
            TxtDiagnosticCard.Foreground = System.Windows.Media.Brushes.Orange;
            TxtDiagnosticCard.Text = "❓ 未在 37,000 张万智牌词库中找到匹配项";
        }
        else
        {
            TxtDiagnosticCard.Foreground = System.Windows.Media.Brushes.Gray;
            TxtDiagnosticCard.Text = "(等待卡牌)";
        }

        TxtDiagnosticLatency.Text = $"{elapsedMs} ms";
    }

    private void ChkRequireCtrl_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        bool require = ChkRequireCtrl.IsChecked == true;
        _config.RequireCtrlKey = require;
        _config.Save();
        _onCtrlModifierChanged?.Invoke(require);
    }

    private void BtnUnlockCaptureBox_Click(object sender, RoutedEventArgs e)
    {
        _onUnlockCaptureBox?.Invoke();
    }

    private void BtnLockCaptureBox_Click(object sender, RoutedEventArgs e)
    {
        _onLockCaptureBox?.Invoke();
    }

    private void BtnCalibrateFull_Click(object sender, RoutedEventArgs e)
    {
        _onCalibrateFull?.Invoke();
    }

    private void ChkShowBorder_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        bool show = ChkShowBorder.IsChecked == true;
        _config.ShowCaptureBoxBorder = show;
        _config.Save();
        _onToggleBorder?.Invoke(show);
    }

    private void RbPos_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;

        OverlayPositionMode pos = OverlayPositionMode.FollowCursor;
        if (RbPosBesidePreview.IsChecked == true) pos = OverlayPositionMode.BesidePreview;
        else if (RbPosCustomFixed.IsChecked == true) pos = OverlayPositionMode.CustomFixed;

        _config.PositionMode = pos;
        _config.Save();
        _onPosModeChanged?.Invoke(pos);
    }

    private void ChkAvoidCardZoom_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _config.AvoidCardZoom = ChkAvoidCardZoom.IsChecked == true;
        _config.Save();
    }

    private void BtnMoveOverlay_Click(object sender, RoutedEventArgs e)
    {
        _onMoveOverlay?.Invoke();
    }

    private void CmbBgColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbBgColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.BgColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtOpacityValue == null) return;
        double op = Math.Round(e.NewValue, 2);
        TxtOpacityValue.Text = $"{Math.Round(op * 100)}%";
        if (_config != null)
        {
            _config.Opacity = op;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void SliderNameZhSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtNameZhSizeValue == null) return;
        int size = (int)e.NewValue;
        TxtNameZhSizeValue.Text = $"{size} px";
        if (_config != null)
        {
            _config.NameZhSize = size;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void CmbNameZhColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbNameZhColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.NameZhColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private void SliderNameEnSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtNameEnSizeValue == null) return;
        int size = (int)e.NewValue;
        TxtNameEnSizeValue.Text = $"{size} px";
        if (_config != null)
        {
            _config.NameEnSize = size;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void CmbNameEnColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbNameEnColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.NameEnColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private void SliderTypeSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtTypeSizeValue == null) return;
        int size = (int)e.NewValue;
        TxtTypeSizeValue.Text = $"{size} px";
        if (_config != null)
        {
            _config.TypeSize = size;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void CmbTypeColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbTypeColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.TypeColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private void SliderRulesSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtRulesSizeValue == null) return;
        int size = (int)e.NewValue;
        TxtRulesSizeValue.Text = $"{size} px";
        if (_config != null)
        {
            _config.RulesSize = size;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void CmbRulesColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbRulesColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.RulesColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private void ChkShowEnRules_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        bool show = ChkShowEnRules.IsChecked == true;
        PanelEnRulesOptions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _config.ShowEnRules = show;
        _config.Save();
        _onStyleChanged?.Invoke();
    }

    private void SliderEnRulesSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtEnRulesSizeValue == null) return;
        int size = (int)e.NewValue;
        TxtEnRulesSizeValue.Text = $"{size} px";
        if (_config != null)
        {
            _config.EnRulesSize = size;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void CmbEnRulesColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbEnRulesColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.EnRulesColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private void SliderPtSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi || TxtPtSizeValue == null) return;
        int size = (int)e.NewValue;
        TxtPtSizeValue.Text = $"{size} px";
        if (_config != null)
        {
            _config.PtSize = size;
            _config.Save();
        }
        _onStyleChanged?.Invoke();
    }

    private void CmbPtColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (CmbPtColor.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.PtColor = item.Tag.ToString()!;
            _config.Save();
            _onStyleChanged?.Invoke();
        }
    }

    private async void BtnTestCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_onTestCaptureRequested != null)
        {
            var res = await _onTestCaptureRequested();
            UpdateDiagnostic(res.bmp, res.ocr, res.card, res.elapsed);
        }
    }

    private void BtnOpenConfig_Click(object sender, RoutedEventArgs e)
    {
        string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MTGOOverlay");
        if (Directory.Exists(configDir))
        {
            Process.Start("explorer.exe", configDir);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        IsSettingsVisible = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Don't destroy window, just hide it so it can be re-shown instantly
        e.Cancel = true;
        Hide();
        IsSettingsVisible = false;
    }
}
