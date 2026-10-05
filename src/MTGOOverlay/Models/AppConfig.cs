using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGOOverlay.Models;

public enum TriggerMode
{
    Hover = 0,
    PreviewPane = 1,
    HoldCtrl = 2
}

public class AppConfig
{
    public TriggerMode Mode { get; set; } = TriggerMode.Hover;

    public bool LockPosition { get; set; } = false;

    public double FixedLeft { get; set; } = 100;

    public double FixedTop { get; set; } = 100;

    public double Width { get; set; } = 380;

    public double Height { get; set; } = 460;

    public double Opacity { get; set; } = 0.94;

    public int DebounceMs { get; set; } = 150;

    // Card preview pane rectangle relative to MTGO window
    public int PreviewRelX { get; set; } = 0;
    public int PreviewRelY { get; set; } = 0;
    public int PreviewWidth { get; set; } = 0;
    public int PreviewHeight { get; set; } = 0;

    [JsonIgnore]
    public bool HasValidPreviewRect => PreviewWidth > 40 && PreviewHeight > 40;

    public Rectangle GetPreviewAbsoluteRect(Rectangle mtgoWindowRect)
    {
        return new Rectangle(
            mtgoWindowRect.Left + PreviewRelX,
            mtgoWindowRect.Top + PreviewRelY,
            PreviewWidth,
            PreviewHeight);
    }

    public void SetPreviewFromAbsolute(Rectangle absoluteRect, Rectangle mtgoWindowRect)
    {
        PreviewRelX = absoluteRect.Left - mtgoWindowRect.Left;
        PreviewRelY = absoluteRect.Top - mtgoWindowRect.Top;
        PreviewWidth = absoluteRect.Width;
        PreviewHeight = absoluteRect.Height;
    }

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MTGOOverlay");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null) return config;
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
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}
