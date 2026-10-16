using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MTGOOverlay.Models;

namespace MTGOOverlay.Native;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AppConfig _config;
    private readonly Action<TriggerMode>? _onTriggerModeChanged;
    private readonly Action<OverlayPositionMode> _onPositionModeChanged;
    private readonly Action _onStartMoveOverlay;
    private readonly Action _onCalibrateRequested;
    private readonly Action _onAdjustCaptureBoxRequested;
    private readonly Action<bool> _onLockCaptureBoxToggle;
    private readonly Action<bool>? _onToggleCaptureBoxBorder;
    private readonly Action? _onOpenSettingsRequested;
    private readonly Action<double>? _onOpacityChanged;
    private readonly Action? _onExitRequested;
    private readonly Action<bool>? _onCtrlModifierChanged;

    private ToolStripMenuItem? _requireCtrlItem;
    private ToolStripMenuItem? _posFollowCursorItem;
    private ToolStripMenuItem? _posBesidePreviewItem;
    private ToolStripMenuItem? _posFixedItem;

    private ToolStripMenuItem? _lockCaptureBoxItem;
    private ToolStripMenuItem? _showBorderItem;

    public TrayIconManager(
        AppConfig config,
        Action<bool> onCtrlModifierChanged,
        Action<OverlayPositionMode> onPositionModeChanged,
        Action onStartMoveOverlay,
        Action onCalibrateRequested,
        Action onAdjustCaptureBoxRequested,
        Action<bool> onLockCaptureBoxToggle,
        Action<bool> onToggleCaptureBoxBorder,
        Action onOpenSettingsRequested,
        Action<double> onOpacityChanged,
        Action onExitRequested,
        Action<TriggerMode>? onTriggerModeChanged = null)
    {
        _config = config;
        _onTriggerModeChanged = onTriggerModeChanged;
        _onCtrlModifierChanged = onCtrlModifierChanged;
        _onPositionModeChanged = onPositionModeChanged;
        _onStartMoveOverlay = onStartMoveOverlay;
        _onCalibrateRequested = onCalibrateRequested;
        _onAdjustCaptureBoxRequested = onAdjustCaptureBoxRequested;
        _onLockCaptureBoxToggle = onLockCaptureBoxToggle;
        _onToggleCaptureBoxBorder = onToggleCaptureBoxBorder;
        _onOpenSettingsRequested = onOpenSettingsRequested;
        _onOpacityChanged = onOpacityChanged;
        _onExitRequested = onExitRequested;

        _notifyIcon = new NotifyIcon
        {
            Text = "MTGO CN Overlay (运行中 - 双击打开设置)",
            Visible = true,
            Icon = LoadUnifiedAppIcon()
        };

        BuildContextMenu();

        _notifyIcon.DoubleClick += (s, e) => _onOpenSettingsRequested?.Invoke();
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        // 1. Title item (disabled)
        var titleItem = new ToolStripMenuItem("🎴 MTGO CN Overlay")
        {
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(titleItem);

        // Settings Window launcher item
        var settingsItem = new ToolStripMenuItem("⚙️ 打开设置面板 (Settings)...", null, (s, e) => _onOpenSettingsRequested?.Invoke())
        {
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());

        // 2. Trigger / Capture Mode
        var modeItem = new ToolStripMenuItem("🔲 Card Preview 停靠窗锚定 (当前模式)")
        {
            Enabled = false,
            Checked = true
        };
        menu.Items.Add(modeItem);
        menu.Items.Add(new ToolStripSeparator());

        // 3. Ctrl Modifier Toggle
        _requireCtrlItem = new ToolStripMenuItem("⌨️ 仅按住 Ctrl 键时弹出浮窗", null, (s, e) =>
        {
            bool req = !_config.RequireCtrlKey;
            _config.RequireCtrlKey = req;
            _config.Save();
            if (s is ToolStripMenuItem item) item.Checked = req;
            _onCtrlModifierChanged?.Invoke(req);
        })
        {
            Checked = _config.RequireCtrlKey
        };
        menu.Items.Add(_requireCtrlItem);
        menu.Items.Add(new ToolStripSeparator());

        // 3. Position mode selection
        var posMenu = new ToolStripMenuItem("📍 浮窗显示位置 (Position)");

        _posFollowCursorItem = new ToolStripMenuItem("智能追随鼠标（自适应左右侧）", null, (s, e) => SelectPositionMode(OverlayPositionMode.FollowCursor))
        {
            Checked = _config.PositionMode == OverlayPositionMode.FollowCursor
        };
        _posBesidePreviewItem = new ToolStripMenuItem("停靠在 Card Preview 预览窗旁", null, (s, e) => SelectPositionMode(OverlayPositionMode.BesidePreview))
        {
            Checked = _config.PositionMode == OverlayPositionMode.BesidePreview
        };
        _posFixedItem = new ToolStripMenuItem("固定在自定义坐标", null, (s, e) => SelectPositionMode(OverlayPositionMode.CustomFixed))
        {
            Checked = _config.PositionMode == OverlayPositionMode.CustomFixed
        };

        var moveItem = new ToolStripMenuItem("✋ 拖拽调整并固定浮窗坐标...", null, (s, e) => _onStartMoveOverlay?.Invoke());

        posMenu.DropDownItems.Add(_posFollowCursorItem);
        posMenu.DropDownItems.Add(_posBesidePreviewItem);
        posMenu.DropDownItems.Add(_posFixedItem);
        posMenu.DropDownItems.Add(new ToolStripSeparator());
        posMenu.DropDownItems.Add(moveItem);
        menu.Items.Add(posMenu);

        // 4. Capture Box Menu
        var captureBoxMenu = new ToolStripMenuItem("🔲 卡牌识别框 (Capture Box)");
        var adjustBoxItem = new ToolStripMenuItem("✋ 调整识别框位置与大小...", null, (s, e) => _onAdjustCaptureBoxRequested?.Invoke());
        _lockCaptureBoxItem = new ToolStripMenuItem("🔒 锁定识别框 (开启鼠标穿透)", null, (s, e) =>
        {
            bool newLocked = !_config.CaptureBoxLocked;
            _onLockCaptureBoxToggle?.Invoke(newLocked);
        })
        {
            Checked = _config.CaptureBoxLocked
        };
        _showBorderItem = new ToolStripMenuItem("👁️ 常驻显示识别框边框", null, (s, e) =>
        {
            bool newBorder = !_config.ShowCaptureBoxBorder;
            _onToggleCaptureBoxBorder?.Invoke(newBorder);
        })
        {
            Checked = _config.ShowCaptureBoxBorder
        };

        captureBoxMenu.DropDownItems.Add(adjustBoxItem);
        captureBoxMenu.DropDownItems.Add(_lockCaptureBoxItem);
        captureBoxMenu.DropDownItems.Add(_showBorderItem);
        captureBoxMenu.DropDownItems.Add(new ToolStripSeparator());
        captureBoxMenu.DropDownItems.Add(new ToolStripMenuItem("📐 全屏重新框选卡名横条...", null, (s, e) => _onCalibrateRequested?.Invoke()));
        menu.Items.Add(captureBoxMenu);

        // 5. Opacity submenu
        var opacityMenu = new ToolStripMenuItem("🎨 浮窗透明度 (Opacity)");
        double[] opacities = new[] { 1.0, 0.94, 0.85, 0.75 };
        foreach (var op in opacities)
        {
            string label = $"{Math.Round(op * 100)}%";
            if (Math.Abs(op - 0.94) < 0.01) label += " (推荐)";
            var item = new ToolStripMenuItem(label, null, (s, e) =>
            {
                _config.Opacity = op;
                _config.Save();
                _onOpacityChanged?.Invoke(op);
                UpdateOpacityCheckmarks(opacityMenu, op);
            })
            {
                Checked = Math.Abs(_config.Opacity - op) < 0.03
            };
            opacityMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(opacityMenu);

        menu.Items.Add(new ToolStripSeparator());

        // 6. Open config directory
        menu.Items.Add(new ToolStripMenuItem("📁 打开配置文件目录", null, (s, e) =>
        {
            string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MTGOOverlay");
            if (Directory.Exists(configDir))
            {
                Process.Start("explorer.exe", configDir);
            }
        }));

        menu.Items.Add(new ToolStripSeparator());

        // 7. Exit
        menu.Items.Add(new ToolStripMenuItem("❌ 退出程序 (Exit)", null, (s, e) => _onExitRequested?.Invoke()));

        _notifyIcon.ContextMenuStrip = menu;
    }

    private void SelectPositionMode(OverlayPositionMode posMode)
    {
        _config.PositionMode = posMode;
        _config.Save();

        RefreshPositionUI(posMode);
        _onPositionModeChanged?.Invoke(posMode);
    }

    public void RefreshPositionUI(OverlayPositionMode posMode)
    {
        if (_posFollowCursorItem != null) _posFollowCursorItem.Checked = posMode == OverlayPositionMode.FollowCursor;
        if (_posBesidePreviewItem != null) _posBesidePreviewItem.Checked = posMode == OverlayPositionMode.BesidePreview;
        if (_posFixedItem != null) _posFixedItem.Checked = posMode == OverlayPositionMode.CustomFixed;
    }

    public void RefreshCtrlModifierUI(bool require)
    {
        if (_requireCtrlItem != null) _requireCtrlItem.Checked = require;
    }

    private void SelectTriggerMode(TriggerMode mode)
    {
        _config.Mode = mode;
        _config.Save();
        RefreshModeUI(mode);
        _onTriggerModeChanged?.Invoke(mode);
    }

    public void RefreshModeUI(TriggerMode mode)
    {
    }

    public void RefreshCaptureBoxLocked(bool locked)
    {
        if (_lockCaptureBoxItem != null) _lockCaptureBoxItem.Checked = locked;
    }

    public void RefreshCaptureBoxBorder(bool showBorder)
    {
        if (_showBorderItem != null) _showBorderItem.Checked = showBorder;
    }

    private static void UpdateOpacityCheckmarks(ToolStripMenuItem menu, double selected)
    {
        foreach (ToolStripMenuItem item in menu.DropDownItems)
        {
            item.Checked = false;
        }
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, icon);
    }

    private static Icon LoadUnifiedAppIcon()
    {
        try
        {
            string icoFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoFile))
            {
                return new Icon(icoFile);
            }
        }
        catch { }

        try
        {
            string? exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null) return extracted;
            }
        }
        catch { }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brushBg = new SolidBrush(Color.FromArgb(26, 28, 33));
            g.FillEllipse(brushBg, 1, 1, 30, 30);

            using var penRing = new Pen(Color.FromArgb(255, 173, 66), 2);
            g.DrawEllipse(penRing, 2, 2, 28, 28);

            using var font = new Font("Segoe UI", 12, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString("M", font, textBrush, 6, 4);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
