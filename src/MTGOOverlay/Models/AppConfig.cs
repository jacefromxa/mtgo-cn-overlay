using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGOOverlay.Models;

public enum TriggerMode
{
    PreviewPane = 1,    // 手动识别框锚定 (针对 Card Preview 固定预览窗)
    HoldCtrl = 2,       // 按住 Ctrl 取词 (兼容保留)
    Hover = 0,          // (已废弃，自动回退到 PreviewPane)
    Dual = 3            // (已废弃，自动回退到 PreviewPane)
}

public enum OverlayPositionMode
{
    FollowCursor = 0,     // 智能追随鼠标（自适应屏幕左右侧）
    BesidePreview = 1,    // 停靠在 Card Preview 预览窗旁
    CustomFixed = 2       // 固定在自定义屏幕坐标
}

public class AppConfig
{
    private TriggerMode _mode = TriggerMode.PreviewPane;
    public TriggerMode Mode
    {
        get => (_mode == TriggerMode.Hover || _mode == TriggerMode.Dual) ? TriggerMode.PreviewPane : _mode;
        set => _mode = value;
    }

    public bool RequireCtrlKey { get; set; } = false;

    public OverlayPositionMode PositionMode { get; set; } = OverlayPositionMode.FollowCursor;

    public bool LockPosition
    {
        get => PositionMode == OverlayPositionMode.CustomFixed;
        set => PositionMode = value ? OverlayPositionMode.CustomFixed : OverlayPositionMode.FollowCursor;
    }

    /// <summary>
    /// Whether to automatically avoid MTGO's native in-game floating card zoom preview.
    /// When true, positions the translation overlay immediately beside MTGO's enlarged card image,
    /// preventing mutual overlap.
    /// </summary>
    public bool AvoidCardZoom { get; set; } = true;

    /// <summary>
    /// Estimated width of MTGO's native floating zoom card in logical pixels (default 260px).
    /// </summary>
    public double CardZoomWidth { get; set; } = 260;

    public double FixedLeft { get; set; } = 100;

    public double FixedTop { get; set; } = 100;

    public double Width { get; set; } = 380;

    public double Height { get; set; } = 460;

    public double Opacity { get; set; } = 0.85;
    public string BgColor { get; set; } = "#1A1C21";

    // Card Names (Always bilingual)
    public int NameZhSize { get; set; } = 16;
    public string NameZhColor { get; set; } = "#FFAD42";
    public int NameEnSize { get; set; } = 12;
    public string NameEnColor { get; set; } = "#A1A1AA";

    // Type Line
    public int TypeSize { get; set; } = 12;
    public string TypeColor { get; set; } = "#A1A1AA";

    // Rules / Abilities Text
    public int RulesSize { get; set; } = 13;
    public string RulesColor { get; set; } = "#D4D4D8";
    public bool ShowEnRules { get; set; } = true;
    public int EnRulesSize { get; set; } = 12;
    public string EnRulesColor { get; set; } = "#94A3B8";

    // Power / Toughness / Loyalty
    public int PtSize { get; set; } = 13;
    public string PtColor { get; set; } = "#FFFFFF";

    // Backward compatibility
    [JsonIgnore]
    public bool ShowEnName { get => true; set { } }
    [JsonIgnore]
    public int EnNameSize { get => NameEnSize; set => NameEnSize = value; }
    [JsonIgnore]
    public string EnNameColor { get => NameEnColor; set => NameEnColor = value; }
    [JsonIgnore]
    public int NameSize { get => NameZhSize; set => NameZhSize = value; }
    [JsonIgnore]
    public int TextSize { get => RulesSize; set => RulesSize = value; }

    public int DebounceMs { get; set; } = 150;

    // Card title capture box rectangle in desktop absolute coordinates
    public int CaptureBoxX { get; set; } = 40;
    public int CaptureBoxY { get; set; } = 120;
    public int CaptureBoxWidth { get; set; } = 280;
    public int CaptureBoxHeight { get; set; } = 34;

    // Backwards compatibility properties (delegating to absolute coordinates)
    [JsonIgnore]
    public int PreviewRelX { get => CaptureBoxX; set => CaptureBoxX = value; }
    [JsonIgnore]
    public int PreviewRelY { get => CaptureBoxY; set => CaptureBoxY = value; }
    [JsonIgnore]
    public int PreviewWidth { get => CaptureBoxWidth; set => CaptureBoxWidth = value; }
    [JsonIgnore]
    public int PreviewHeight { get => CaptureBoxHeight; set => CaptureBoxHeight = value; }

    public bool CaptureBoxLocked { get; set; } = false;
    public bool ShowCaptureBoxBorder { get; set; } = true;
    public bool ShowCaptureBox { get; set; } = true;

    [JsonIgnore]
    public bool HasValidPreviewRect => CaptureBoxWidth >= 30 && CaptureBoxHeight >= 12;

    public Rectangle GetCaptureBoxRect()
    {
        return new Rectangle(CaptureBoxX, CaptureBoxY, CaptureBoxWidth, CaptureBoxHeight);
    }

    public Rectangle GetPreviewAbsoluteRect(Rectangle mtgoWindowRect)
    {
        return GetCaptureBoxRect();
    }

    public void SetPreviewFromAbsolute(Rectangle absoluteRect, Rectangle mtgoWindowRect)
    {
        CaptureBoxX = absoluteRect.Left;
        CaptureBoxY = absoluteRect.Top;
        CaptureBoxWidth = absoluteRect.Width;
        CaptureBoxHeight = absoluteRect.Height;
    }

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MTGOOverlay");

    private static readonly string DefaultConfigFilePath = Path.Combine(ConfigDir, "config.json");

    public static string? CustomConfigPath { get; set; }
    private static string EffectiveConfigPath => CustomConfigPath ?? DefaultConfigFilePath;
    private static string EffectiveConfigDir => CustomConfigPath != null ? Path.GetDirectoryName(CustomConfigPath)! : ConfigDir;

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(EffectiveConfigPath))
            {
                string json = File.ReadAllText(EffectiveConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    if (config.CaptureBoxWidth <= 0 || config.CaptureBoxHeight <= 0)
                    {
                        config.CaptureBoxX = 40;
                        config.CaptureBoxY = 120;
                        config.CaptureBoxWidth = 280;
                        config.CaptureBoxHeight = 34;
                        config.CaptureBoxLocked = false;
                    }
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
        }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            string dir = EffectiveConfigDir;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(EffectiveConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}
