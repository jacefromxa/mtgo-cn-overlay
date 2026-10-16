using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace TestRunner;

public static class IconGenerator
{
    public static void GenerateAppIcon(string outputPath)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var pngDataList = new List<byte[]>();

        foreach (int size in sizes)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                float pad = size * 0.04f;
                float d = size - pad * 2f;

                // 1. Dark circular card emblem base
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(pad, pad, d, d);
                    using var brushBg = new LinearGradientBrush(
                        new PointF(0, 0), new PointF(size, size),
                        Color.FromArgb(32, 36, 46), Color.FromArgb(16, 18, 24));
                    g.FillPath(brushBg, path);

                    // 2. Radiant Golden Ring
                    float penW = Math.Max(1.5f, size * 0.07f);
                    using var penGold = new LinearGradientBrush(
                        new PointF(0, 0), new PointF(size, size),
                        Color.FromArgb(255, 210, 90), Color.FromArgb(215, 135, 25));
                    using var pen = new Pen(penGold, penW);
                    g.DrawEllipse(pen, pad + penW / 2f, pad + penW / 2f, d - penW, d - penW);
                }

                // 3. Central MTG Emblem: Golden Card Border + "M"
                if (size >= 32)
                {
                    float cardW = size * 0.44f;
                    float cardH = size * 0.58f;
                    float cardX = (size - cardW) / 2f;
                    float cardY = (size - cardH) / 2f;
                    float cardRadius = size * 0.04f;

                    using var cardPath = GetRoundedRectPath(new RectangleF(cardX, cardY, cardW, cardH), cardRadius);
                    using var cardBg = new SolidBrush(Color.FromArgb(220, 20, 22, 28));
                    g.FillPath(cardBg, cardPath);

                    using var cardPen = new Pen(Color.FromArgb(180, 255, 173, 66), Math.Max(1f, size * 0.03f));
                    g.DrawPath(cardPen, cardPath);
                }

                // 4. Central "M" Letter
                float fontSize = size * 0.44f;
                using var font = new Font("Georgia", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Warm gold drop shadow
                var shadowRect = new RectangleF(0, size * 0.03f, size, size);
                using var shadowBrush = new SolidBrush(Color.FromArgb(140, 255, 173, 66));
                g.DrawString("M", font, shadowBrush, shadowRect, sf);

                // High contrast ivory/white text
                var textRect = new RectangleF(0, 0, size, size);
                using var textBrush = new LinearGradientBrush(
                    new PointF(0, size * 0.25f), new PointF(0, size * 0.75f),
                    Color.FromArgb(255, 255, 255), Color.FromArgb(255, 230, 160));
                g.DrawString("M", font, textBrush, textRect, sf);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngDataList.Add(ms.ToArray());
        }

        // Write standard multi-icon Windows .ICO file
        string dir = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR Header
        bw.Write((short)0); // Reserved
        bw.Write((short)1); // Type: 1 = ICO
        bw.Write((short)pngDataList.Count); // Count of images

        int offset = 6 + (16 * pngDataList.Count);
        for (int i = 0; i < pngDataList.Count; i++)
        {
            int sz = sizes[i];
            byte[] data = pngDataList[i];

            bw.Write((byte)(sz >= 256 ? 0 : sz)); // Width
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // Height
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((short)1); // Color planes
            bw.Write((short)32); // Bits per pixel
            bw.Write((int)data.Length); // Image size in bytes
            bw.Write((int)offset); // Offset of image data

            offset += data.Length;
        }

        foreach (var data in pngDataList)
        {
            bw.Write(data);
        }
        bw.Flush();
    }

    private static GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        var arc = new RectangleF(rect.Location, new SizeF(diameter, diameter));

        // Top-left arc
        path.AddArc(arc, 180, 90);
        // Top-right arc
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        // Bottom-right arc
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // Bottom-left arc
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
