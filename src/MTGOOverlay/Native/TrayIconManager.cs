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
    private readonly Action<TriggerMode> _onModeChanged;
    private readonly Action _onCalibrateRequested;
    private readonly Action _onToggleLockPosition;
    private readonly Action _onTestCardRequested;
    private readonly Action<double> _onOpacityChanged;
    private readonly Action _onExitRequested;

    private ToolStripMenuItem? _hoverItem;
    private ToolStripMenuItem? _previewItem;
    private ToolStripMenuItem? _holdCtrlItem;
    private ToolStripMenuItem? _lockPosItem;

    public TrayIconManager(
        AppConfig config,
        Action<TriggerMode> onModeChanged,
        Action onCalibrateRequested,
        Action onToggleLockPosition,
        Action onTestCardRequested,
        Action<double> onOpacityChanged,
        Action onExitRequested)
    {
        _config = config;
        _onModeChanged = onModeChanged;
        _onCalibrateRequested = onCalibrateRequested;
        _onToggleLockPosition = onToggleLockPosition;
        _onTestCardRequested = onTestCardRequested;
        _onOpacityChanged = onOpacityChanged;
        _onExitRequested = onExitRequested;

        _notifyIcon = new NotifyIcon
        {
            Text = "MTGO CN Overlay (运行中)",
            Visible = true,
            Icon = CreateAppIcon()
        };

        BuildContextMenu();

        _notifyIcon.DoubleClick += (s, e) => _onTestCardRequested?.Invoke();
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
        menu.Items.Add(new ToolStripSeparator());

        // 2. Mode selection
        var modeMenu = new ToolStripMenuItem("🎯 取词模式 (Mode)");

        _hoverItem = new ToolStripMenuItem("光标悬停取词 (Hover)", null, (s, e) => SelectMode(TriggerMode.Hover))
        {
            Checked = _config.Mode == TriggerMode.Hover
        };
        _previewItem = new ToolStripMenuItem("Card Preview 停靠窗锚定 (Preview)", null, (s, e) => SelectMode(TriggerMode.PreviewPane))
        {
            Checked = _config.Mode == TriggerMode.PreviewPane
        };
        _holdCtrlItem = new ToolStripMenuItem("按住 Ctrl 键取词 (Hold Ctrl)", null, (s, e) => SelectMode(TriggerMode.HoldCtrl))
        {
            Checked = _config.Mode == TriggerMode.HoldCtrl
        };

        modeMenu.DropDownItems.Add(_hoverItem);
        modeMenu.DropDownItems.Add(_previewItem);
        modeMenu.DropDownItems.Add(_holdCtrlItem);
        menu.Items.Add(modeMenu);

        // 3. Calibrate Preview Rect
        menu.Items.Add(new ToolStripMenuItem("📐 框选校准 Card Preview 区域...", null, (s, e) => _onCalibrateRequested?.Invoke()));

        // 4. Lock overlay position
        _lockPosItem = new ToolStripMenuItem("📌 锁定浮窗当前位置")
        {
            Checked = _config.LockPosition
        };
        _lockPosItem.Click += (s, e) =>
        {
            _config.LockPosition = !_config.LockPosition;
            _config.Save();
            if (_lockPosItem != null) _lockPosItem.Checked = _config.LockPosition;
            _onToggleLockPosition?.Invoke();
        };
        menu.Items.Add(_lockPosItem);

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

        // 6. Test card preview
        menu.Items.Add(new ToolStripMenuItem("👁️ 测试显示示例卡牌", null, (s, e) => _onTestCardRequested?.Invoke()));

        // 7. Open config directory
        menu.Items.Add(new ToolStripMenuItem("📁 打开配置文件目录", null, (s, e) =>
        {
            string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MTGOOverlay");
            if (Directory.Exists(configDir))
            {
                Process.Start("explorer.exe", configDir);
            }
        }));

        menu.Items.Add(new ToolStripSeparator());

        // 8. Exit
        menu.Items.Add(new ToolStripMenuItem("❌ 退出程序 (Exit)", null, (s, e) => _onExitRequested?.Invoke()));

        _notifyIcon.ContextMenuStrip = menu;
    }

    private void SelectMode(TriggerMode mode)
    {
        _config.Mode = mode;
        _config.Save();

        if (_hoverItem != null) _hoverItem.Checked = mode == TriggerMode.Hover;
        if (_previewItem != null) _previewItem.Checked = mode == TriggerMode.PreviewPane;
        if (_holdCtrlItem != null) _holdCtrlItem.Checked = mode == TriggerMode.HoldCtrl;

        _onModeChanged?.Invoke(mode);
    }

    public void RefreshModeUI(TriggerMode mode)
    {
        if (_hoverItem != null) _hoverItem.Checked = mode == TriggerMode.Hover;
        if (_previewItem != null) _previewItem.Checked = mode == TriggerMode.PreviewPane;
        if (_holdCtrlItem != null) _holdCtrlItem.Checked = mode == TriggerMode.HoldCtrl;
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

    private static Icon CreateAppIcon()
    {
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
