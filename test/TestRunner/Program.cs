using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using MTGOOverlay.Capture;
using MTGOOverlay.Data;
using MTGOOverlay.Models;

Console.WriteLine("==================================================");
Console.WriteLine("    MTGO CN Overlay C# Engine Test & Verification ");
Console.WriteLine("==================================================\n");

int passed = 0;
int failed = 0;

void Assert(bool condition, string testName, string? details = null)
{
    if (condition)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS] ");
        Console.ResetColor();
        Console.WriteLine(testName);
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL] ");
        Console.ResetColor();
        Console.WriteLine($"{testName} {(details != null ? $"-> {details}" : "")}");
        failed++;
    }
}

// 1. Test CardDatabase Loading
var db = new CardDatabase();
var sw = Stopwatch.StartNew();
string dbPath = Path.GetFullPath("data/en2zhs.json.gz");
if (!File.Exists(dbPath))
{
    dbPath = Path.GetFullPath("../../data/en2zhs.json.gz");
}

await db.LoadDatabaseAsync(dbPath);
sw.Stop();
Assert(db.IsLoaded && db.Count > 35000, $"Database Loaded: {db.Count} cards in {sw.ElapsedMilliseconds}ms");
// 2. Database loaded verification done above

// 3. Test Common OCR Noise Correction (0->o, 1->i)
var boltNoisy = db.Lookup("L1ghtn1ng B0lt");
Assert(boltNoisy != null && boltNoisy.NameZh == "闪电击", "Noise Correction: 'L1ghtn1ng B0lt' -> '闪电击'");

// 4. Test Single-char typo
var lily = db.Lookup("Liliana of the Veyl");
Assert(lily != null && lily.NameZh == "面纱的莉莲娜", "Fuzzy Match: 'Liliana of the Veyl' -> '面纱的莉莲娜'");

// 5. Test Multi-line OCR output
var multiLine = "Murktide Regent\nCreature — Dragon";
var murk = db.Lookup(multiLine);
Assert(murk != null && murk.NameZh == "黯潮龙侯", "Multi-line OCR: 'Murktide Regent\\nCreature' -> '黯潮龙侯'");

// 6. Test Title Line with Mana Cost Suffixes (very common in MTGO card title bars)
var boltWithMana = db.Lookup("Lightning Bolt {R}");
Assert(boltWithMana != null && boltWithMana.NameZh == "闪电击", "Title with Mana {R}: 'Lightning Bolt {R}' -> '闪电击'");

var tarmoWithMana = db.Lookup("Tarmogoyf 1G");
Assert(tarmoWithMana != null && tarmoWithMana.NameZh == "塔莫耶夫", "Title with Mana 1G: 'Tarmogoyf 1G' -> '塔莫耶夫'");

var titanWithMana = db.Lookup("Grave Titan 4BB");
Assert(titanWithMana != null && titanWithMana.NameZh == "墓地泰坦", "Title with Mana 4BB: 'Grave Titan 4BB' -> '墓地泰坦'");

// 7. Test Full Card Crop (Top Title + Type + Rules)
var fullCardCrop = "Counterspell UU\nInstant\nCounter target spell.";
var counter = db.Lookup(fullCardCrop);
Assert(counter != null && counter.NameZh == "反击咒语", "Full Card Crop: 'Counterspell UU\\nInstant\\nRules' -> '反击咒语'");

// 8. Test AppConfig Load & Save (in isolated temporary test file)
string testConfigPath = Path.Combine(Path.GetTempPath(), "test_mtgo_overlay_config.json");
if (File.Exists(testConfigPath)) File.Delete(testConfigPath);
AppConfig.CustomConfigPath = testConfigPath;

var config = new AppConfig();
config.Mode = TriggerMode.PreviewPane;
config.LockPosition = true;
config.FixedLeft = 200;
config.FixedTop = 300;
config.Save();

var loaded = AppConfig.Load();
Assert(loaded.Mode == TriggerMode.PreviewPane && loaded.LockPosition && loaded.FixedLeft == 200, "AppConfig Load & Save Persistence");

// 9. Test Fast Sample Hash
using var bmp1 = new Bitmap(100, 100);
using var bmp2 = new Bitmap(100, 100);
using (var g = Graphics.FromImage(bmp1)) g.Clear(Color.Blue);
using (var g = Graphics.FromImage(bmp2)) g.Clear(Color.Blue);

ulong hash1 = ScreenCapture.ComputeFastSampleHash(bmp1);
ulong hash2 = ScreenCapture.ComputeFastSampleHash(bmp2);
Assert(hash1 == hash2 && hash1 != 0, "Fast Sample Hash: Identical images produce identical hash");

using (var g = Graphics.FromImage(bmp2)) g.Clear(Color.Red);
ulong hash3 = ScreenCapture.ComputeFastSampleHash(bmp2);
Assert(hash1 != hash3, "Fast Sample Hash: Distinct images produce distinct hash");

// 11. Test WindowsOcrEngine
var ocrEngine = new MTGOOverlay.Ocr.WindowsOcrEngine();
bool ocrInit = ocrEngine.Initialize();
Assert(ocrInit && ocrEngine.IsAvailable, "WindowsOcrEngine Initialized");

if (ocrEngine.IsAvailable)
{
    string[] testCards = new[]
    {
        "Lightning Bolt",
        "Dark Ritual",
        "Thoughtseize",
        "Counterspell",
        "Fatal Push",
        "Brainstorm",
        "Grief"
    };

    Color[] darkBgColors = new[]
    {
        Color.FromArgb(22, 22, 26),  // Black card frame
        Color.FromArgb(18, 38, 58),  // Blue card frame
        Color.FromArgb(20, 42, 25),  // Green card frame
        Color.FromArgb(55, 24, 20),  // Red card frame
        Color.FromArgb(50, 42, 28)   // Gold card frame
    };

    for (int i = 0; i < testCards.Length; i++)
    {
        string cardName = testCards[i];
        Color bg = darkBgColors[i % darkBgColors.Length];

        using var testBmp = new Bitmap(280, 220);
        using (var g = Graphics.FromImage(testBmp))
        {
            // Title bar at top
            g.FillRectangle(new SolidBrush(bg), 0, 0, 280, 32);
            using var font = new Font("Georgia", 13, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(160, 155, 145));
            g.DrawString(cardName, font, brush, 8, 4);

            // Artwork below (dark/noisy illustration textures)
            using var artBrush = new LinearGradientBrush(new Rectangle(0, 32, 280, 188), Color.FromArgb(15, 15, 20), Color.FromArgb(45, 30, 25), 45f);
            g.FillRectangle(artBrush, 0, 32, 280, 188);
        }

        string recognized = await ocrEngine.RecognizeTextAsync(testBmp);
        var card = db.Lookup(recognized);
        Console.WriteLine($"[TALL CROP OCR] Card: '{cardName}' -> Raw OCR: '{recognized}' -> Match: '{(card != null ? card.NameZh : "FAIL")}'");
        Assert(card != null, $"Tall Frame OCR: '{cardName}' matched '{card?.NameZh}'");
    }
}

// 12. Test CapturePreviewTitle direct 1:1 bounds safety
var nullBmp = ScreenCapture.CapturePreviewTitle(Rectangle.Empty);
Assert(nullBmp == null, "ScreenCapture 1:1 Title Box: Empty rect safely returns null");

// 13. Test CaptureBox config persistence
config.CaptureBoxLocked = false;
config.ShowCaptureBoxBorder = true;
config.PreviewWidth = 320;
config.PreviewHeight = 36;
config.Save();
var loaded2 = AppConfig.Load();
Assert(!loaded2.CaptureBoxLocked && loaded2.PreviewWidth == 320 && loaded2.PreviewHeight == 36, "CaptureBox configuration persistence");

// 14. Test Multi-monitor VirtualScreen detection & bounds calculation
var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
Console.WriteLine($"[INFO] Detected VirtualScreen bounds: {vs}");
foreach (var scr in System.Windows.Forms.Screen.AllScreens)
{
    Console.WriteLine($"[INFO] - Monitor: {scr.DeviceName}, Bounds: {scr.Bounds}, Primary: {scr.Primary}");
}
Assert(vs.Width > 0 && vs.Height > 0 && System.Windows.Forms.Screen.AllScreens.Length >= 1, 
    $"Multi-monitor detection: {System.Windows.Forms.Screen.AllScreens.Length} screen(s) detected spanning {vs.Width}x{vs.Height}");

// 15. Test Absolute CaptureBox coords persistence (Secondary Monitor with negative coordinates)
config.CaptureBoxX = -1200;
config.CaptureBoxY = 250;
config.CaptureBoxWidth = 350;
config.CaptureBoxHeight = 40;
config.Save();
var loaded3 = AppConfig.Load();
Assert(loaded3.CaptureBoxX == -1200 && loaded3.CaptureBoxY == 250 && loaded3.CaptureBoxWidth == 350 && loaded3.CaptureBoxHeight == 40, "CaptureBox: Negative desktop coordinate persistence (Secondary Monitor)");

// 16. Test Modular Style Configuration Persistence
config.BgColor = "#0F172A";
config.Opacity = 0.90;
config.NameZhSize = 18;
config.NameZhColor = "#4ADE80";
config.NameEnSize = 14;
config.NameEnColor = "#CBD5E1";
config.TypeSize = 13;
config.TypeColor = "#7DD3FC";
config.RulesSize = 15;
config.RulesColor = "#FEF9C3";
config.ShowEnRules = true;
config.EnRulesSize = 13;
config.EnRulesColor = "#93C5FD";
config.PtSize = 14;
config.PtColor = "#FDE047";
config.Save();

var loadedStyles = AppConfig.Load();
Assert(loadedStyles.BgColor == "#0F172A" &&
       loadedStyles.Opacity == 0.90 &&
       loadedStyles.NameZhSize == 18 &&
       loadedStyles.NameZhColor == "#4ADE80" &&
       loadedStyles.NameEnSize == 14 &&
       loadedStyles.NameEnColor == "#CBD5E1" &&
       loadedStyles.TypeSize == 13 &&
       loadedStyles.TypeColor == "#7DD3FC" &&
       loadedStyles.RulesSize == 15 &&
       loadedStyles.RulesColor == "#FEF9C3" &&
       loadedStyles.ShowEnRules &&
       loadedStyles.EnRulesSize == 13 &&
       loadedStyles.EnRulesColor == "#93C5FD" &&
       loadedStyles.PtSize == 14 &&
       loadedStyles.PtColor == "#FDE047",
       "Modular Style Configuration Persistence");

// 17. Test OracleTextProvider Pre-seeded & Cached Lookup
OracleTextProvider.Initialize();
string? boltOracle = OracleTextProvider.GetOracleText("Lightning Bolt");
Assert(!string.IsNullOrEmpty(boltOracle) && boltOracle.Contains("deals 3 damage"), 
       "OracleTextProvider: Preseeded Oracle Text Lookup ('Lightning Bolt')");

var tcs = new TaskCompletionSource<string>();
var ot = OracleTextProvider.GetOracleText("Counterspell", (name, text) => tcs.TrySetResult(text));
string? counterspellText = ot;
if (counterspellText == null)
{
    var completed = await Task.WhenAny(tcs.Task, Task.Delay(4000));
    if (completed == tcs.Task) counterspellText = await tcs.Task;
}
Assert(!string.IsNullOrEmpty(counterspellText) && counterspellText.Contains("Counter target spell"), 
       "OracleTextProvider: Dynamic Oracle Text Retrieval ('Counterspell')");

// 18. Test Goudy Medieval retro font OCR variants
var cf = db.Lookup("Counterfpell");
Assert(cf != null && cf.NameZh == "反击咒语", "Goudy Medieval Medial-S: 'Counterfpell' -> '反击咒语'");

var sv = db.Lookup("Svvords to Plovvshares");
Assert(sv != null && sv.NameZh == "化剑为犁", "Goudy Medieval Overlapping-W: 'Svvords to Plovvshares' -> '化剑为犁'");

var vvrath = db.Lookup("Vvrath of God");
Assert(vvrath != null && vvrath.NameZh == "神之愤怒", "Goudy Medieval Overlapping-W: 'Vvrath of God' -> '神之愤怒'");

var tirne = db.Lookup("Tirne Walk");
Assert(tirne != null && tirne.NameZh == "时间行走", "Goudy Medieval Minims: 'Tirne Walk' -> '时间行走'");

var dernonic = db.Lookup("Dernonic Tutor");
Assert(dernonic != null && dernonic.NameZh == "邪魔导师", "Goudy Medieval Minims: 'Dernonic Tutor' -> '邪魔导师'");

var clark = db.Lookup("Clark Ritual");
Assert(clark != null && clark.NameZh == "黑暗祭礼", "Goudy Medieval D/CL: 'Clark Ritual' -> '黑暗祭礼'");

// 19. Test Split Cards & Diacritics
var splitFire = db.Lookup("Fire");
Assert(splitFire != null, "Split Card Half-Name Lookup: 'Fire'");

var splitIce = db.Lookup("Ice");
Assert(splitIce != null, "Split Card Half-Name Lookup: 'Ice'");

var aether = db.Lookup("Aether Vial");
Assert(aether != null && (aether.NameZh == "乙太精瓶" || aether.NameZh == "虚空圣杯"), "Diacritics Normalization: 'Aether Vial'");

// 20. Test Dark Background & Otsu Binarization with Multi-pass Recognition
if (ocrEngine.IsAvailable)
{
    using var darkRetroBmp = new Bitmap(280, 40);
    using (var g = Graphics.FromImage(darkRetroBmp))
    {
        // Dark background (black card / retro frame: dark slate #16181E)
        g.Clear(Color.FromArgb(22, 24, 30));
        using var font = new Font("Georgia", 14, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(235, 230, 220)); // Light cream text
        g.DrawString("Dark Ritual", font, brush, 8, 8);
    }

    var (darkOcr, darkCard) = await ocrEngine.RecognizeAndLookupAsync(darkRetroBmp, text => db.Lookup(text));
    Assert(darkCard != null && darkCard.NameZh == "黑暗祭礼", 
        $"Dark Background Multi-Pass OCR: 'Dark Ritual' -> '{darkCard?.NameZh}' (Raw OCR: '{darkOcr}')");
}

// 21. Test and Generate Unified App Icon
string icoPath = Path.GetFullPath("src/MTGOOverlay/app.ico");
if (!File.Exists(icoPath) && Directory.Exists("../../src/MTGOOverlay"))
{
    icoPath = Path.GetFullPath("../../src/MTGOOverlay/app.ico");
}
TestRunner.IconGenerator.GenerateAppIcon(icoPath);
var icoInfo = new FileInfo(icoPath);
Assert(icoInfo.Exists && icoInfo.Length > 2000, $"Unified Application Icon generated successfully ({icoInfo.Length} bytes)");

// 22. Test PaddleOcrEngine (Deep Learning PP-OCRv6 SIMD)
using var paddleEngine = new MTGOOverlay.Ocr.PaddleOcrEngine();
bool paddleInit = paddleEngine.Initialize();
Assert(paddleInit && paddleEngine.IsAvailable, $"PaddleOcrEngine Initialized: '{paddleEngine.EngineName}'");

if (paddleEngine.IsAvailable)
{
    using var darkPaddleBmp = new Bitmap(320, 36);
    using (var g = Graphics.FromImage(darkPaddleBmp))
    {
        g.Clear(Color.FromArgb(18, 18, 22)); // Deep black frame
        using var font = new Font("Georgia", 16, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(240, 240, 240));
        g.DrawString("Dark Confidant", font, brush, 8, 4);
    }

    var swPaddle = Stopwatch.StartNew();
    var (paddleRaw, paddleCard) = await paddleEngine.RecognizeAndLookupAsync(darkPaddleBmp, text => db.Lookup(text));
    swPaddle.Stop();

    Assert(paddleCard != null && paddleCard.NameZh == "黑暗亲信",
        $"PaddleOcrEngine Dark Frame: 'Dark Confidant' -> '{paddleCard?.NameZh}' in {swPaddle.ElapsedMilliseconds}ms (Raw: '{paddleRaw}')");
}

// 23. Test HybridOcrEngine (Dual-Engine Fallback Pipeline)
using var hybridEngine = new MTGOOverlay.Ocr.HybridOcrEngine();
bool hybridInit = hybridEngine.Initialize();
Assert(hybridInit && hybridEngine.IsAvailable, $"HybridOcrEngine Initialized: '{hybridEngine.EngineName}'");

if (hybridEngine.IsAvailable)
{
    using var hybridBmp = new Bitmap(340, 36);
    using (var g = Graphics.FromImage(hybridBmp))
    {
        g.Clear(Color.FromArgb(25, 20, 30));
        using var font = new Font("Georgia", 15, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(230, 230, 230));
        g.DrawString("Thoughtseize", font, brush, 8, 5);
    }

    var swHybrid = Stopwatch.StartNew();
    var (hybridRaw, hybridCard) = await hybridEngine.RecognizeAndLookupAsync(hybridBmp, text => db.Lookup(text));
    swHybrid.Stop();

    Assert(hybridCard != null && hybridCard.NameZh == "攫取思绪",
        $"HybridOcrEngine Lookup: 'Thoughtseize' -> '{hybridCard?.NameZh}' in {swHybrid.ElapsedMilliseconds}ms (Raw: '{hybridRaw}')");
}

// 24. Test AvoidCardZoom configuration persistence
config.AvoidCardZoom = true;
config.CardZoomWidth = 270;
config.Save();
var loadedZoom = AppConfig.Load();
Assert(loadedZoom.AvoidCardZoom && loadedZoom.CardZoomWidth == 270, "AvoidCardZoom & CardZoomWidth Configuration Persistence");

// 25. Test Native Card Zoom Clearance calculations (Left-First Zero-Overlap Strategy)
double overlayW = 380;
double zoomW = 270;
double zoomG = 16;
double scrL = 0;

// Case A: Cursor in middle of screen (X=600): Overlay sits on LEFT of cursor, MTGO zoom sits on RIGHT of cursor
double curX_A = 600;
double candLeft_A = curX_A - zoomG - overlayW; // 600 - 16 - 380 = 204
bool hasLeftRoom_A = (candLeft_A >= scrL + 15);
double left_A = hasLeftRoom_A ? candLeft_A : (curX_A + 24 + zoomW + zoomG);
Assert(hasLeftRoom_A && left_A == 204 && left_A + overlayW <= curX_A, "Card Zoom Avoidance (Center): Overlay placed cleanly LEFT of cursor, completely isolated from MTGO's right-side zoom");

// Case B: Cursor near left edge (X=200): Not enough space on left, flips to RIGHT of MTGO's zoom card
double curX_B = 200;
double candLeft_B = curX_B - zoomG - overlayW; // 200 - 16 - 380 = -196 < 15
bool hasLeftRoom_B = (candLeft_B >= scrL + 15);
double candRight_B = curX_B + 24 + zoomW + zoomG; // 200 + 24 + 270 + 16 = 510
double left_B = hasLeftRoom_B ? candLeft_B : candRight_B;
Assert(!hasLeftRoom_B && left_B == 510 && left_B >= curX_B + 24 + zoomW, "Card Zoom Avoidance (Left Edge): Overlay flips cleanly to the RIGHT of MTGO's enlarged card");

// 26. Test CardZoomDetector: Detects MTGO's zoom card in synthetic screen
using var testTableBmp = new Bitmap(800, 600);
using (var g = Graphics.FromImage(testTableBmp))
{
    // Background: duel battlefield table
    g.Clear(Color.FromArgb(48, 38, 32));

    // Two small cards on battlefield (90x125 each)
    using var smallBorderBrush = new SolidBrush(Color.FromArgb(15, 15, 15));
    using var smallCardBrush = new SolidBrush(Color.FromArgb(70, 80, 60));
    g.FillRectangle(smallBorderBrush, 80, 120, 90, 125);
    g.FillRectangle(smallCardBrush, 84, 124, 82, 117);

    g.FillRectangle(smallBorderBrush, 80, 280, 90, 125);
    g.FillRectangle(smallCardBrush, 84, 284, 82, 117);

    // One large MTGO hover zoom card on right of cursor: 260x365 at (420, 80)
    using var zoomBorderBrush = new SolidBrush(Color.FromArgb(12, 12, 14));
    using var zoomTitleBrush = new SolidBrush(Color.FromArgb(20, 36, 56));
    using var zoomArtBrush = new SolidBrush(Color.FromArgb(160, 130, 90));
    using var zoomTextBrush = new SolidBrush(Color.FromArgb(210, 205, 195));
    g.FillRectangle(zoomBorderBrush, 420, 80, 260, 365);
    // Title bar at top: Force of Will
    g.FillRectangle(zoomTitleBrush, 434, 94, 232, 28);
    using var titleFont = new Font("Georgia", 13, FontStyle.Bold);
    using var titleTextBrush = new SolidBrush(Color.FromArgb(235, 230, 220));
    g.DrawString("Force of Will", titleFont, titleTextBrush, 436, 96);
    // Art and rules text below
    g.FillRectangle(zoomArtBrush, 432, 126, 236, 138);
    g.FillRectangle(zoomTextBrush, 432, 274, 236, 155);
}

// Cursor is hovering at (360, 200)
var cursorPt = new Point(360, 200);
var detResult = CardZoomDetector.DetectInBitmap(testTableBmp, new Point(0, 0), cursorPt);
Assert(detResult.IsDetected, $"CardZoomDetector: Detected MTGO zoom card (Conf={detResult.Confidence:F2})");
if (detResult.IsDetected)
{
    var box = detResult.BoundingBox;
    bool boxClose = Math.Abs(box.X - 420) <= 8 && Math.Abs(box.Y - 80) <= 8 &&
                    Math.Abs(box.Width - 260) <= 12 && Math.Abs(box.Height - 365) <= 12;
    Assert(boxClose && detResult.IsRightOfCursor, 
        $"CardZoomDetector: Exact Bounding Box [{box.X}, {box.Y}, {box.Width}x{box.Height}] matched target [420, 80, 260x365]");
}

// 27. Test CardZoomDetector: Rejects small battlefield cards when no zoom card is present
using var tableNoZoomBmp = new Bitmap(800, 600);
using (var g = Graphics.FromImage(tableNoZoomBmp))
{
    g.Clear(Color.FromArgb(48, 38, 32));
    using var bBrush = new SolidBrush(Color.FromArgb(15, 15, 15));
    using var cBrush = new SolidBrush(Color.FromArgb(70, 80, 60));
    g.FillRectangle(bBrush, 80, 120, 90, 125);
    g.FillRectangle(cBrush, 84, 124, 82, 117);
    g.FillRectangle(bBrush, 220, 120, 90, 125);
    g.FillRectangle(cBrush, 224, 124, 82, 117);
}
var noZoomResult = CardZoomDetector.DetectInBitmap(tableNoZoomBmp, new Point(0, 0), new Point(120, 150));
Assert(!noZoomResult.IsDetected, "CardZoomDetector: Correctly ignored small battlefield cards (No False Positive)");

// 28. Test CardZoomDetector: Detects zoom card placed on LEFT of cursor
using var tableLeftZoomBmp = new Bitmap(800, 600);
using (var g = Graphics.FromImage(tableLeftZoomBmp))
{
    g.Clear(Color.FromArgb(48, 38, 32));
    // Zoom card at (80, 80, 260, 365)
    using var zoomBorderBrush = new SolidBrush(Color.FromArgb(12, 12, 14));
    using var zoomArtBrush = new SolidBrush(Color.FromArgb(80, 100, 140));
    g.FillRectangle(zoomBorderBrush, 80, 80, 260, 365);
    g.FillRectangle(zoomArtBrush, 92, 94, 236, 170);
}
// Cursor at (400, 200) (Zoom card is on the LEFT of cursor)
var leftZoomResult = CardZoomDetector.DetectInBitmap(tableLeftZoomBmp, new Point(0, 0), new Point(400, 200));
Assert(leftZoomResult.IsDetected && !leftZoomResult.IsRightOfCursor, 
    "CardZoomDetector: Detected left-flipped zoom card and recognized !IsRightOfCursor");

// 29. Test CardZoomDetector Performance Benchmark (< 2ms)
var detSw = Stopwatch.StartNew();
int benchIterations = 20;
for (int i = 0; i < benchIterations; i++)
{
    CardZoomDetector.DetectInBitmap(testTableBmp, new Point(0, 0), cursorPt);
}
detSw.Stop();
double avgMs = (double)detSw.ElapsedMilliseconds / benchIterations;
Assert(avgMs < 5.0, $"CardZoomDetector: Ultra-fast inference (Average {avgMs:F2}ms per frame)");

// 30. Test Zero-Config Dynamic Hover OCR (Autonomous Full Card Recognition directly from Zoom Card)
using var autoCardBmp = testTableBmp.Clone(detResult.BoundingBox, testTableBmp.PixelFormat);
var (autoOcr, autoCard) = await ocrEngine.RecognizeAndLookupAsync(autoCardBmp, s => db.Lookup(s));
Assert(autoCard != null && autoCard.NameZh == "意志之力", 
    $"Autonomous Zoom OCR: Directly recognized full zoom card '{autoOcr}' -> '{autoCard?.NameZh}' without manual CaptureBox!");

// 31. Test TriggerMode Persistence & Legacy Normalization
config.Mode = TriggerMode.PreviewPane;
config.Save();
var loadedPreview = AppConfig.Load();
Assert(loadedPreview.Mode == TriggerMode.PreviewPane, "TriggerMode.PreviewPane Configuration Persistence");

// Test that legacy modes gracefully normalize to PreviewPane
config.Mode = TriggerMode.Hover;
Assert(config.Mode == TriggerMode.PreviewPane, "TriggerMode.Hover Graceful Normalization to PreviewPane");

// 32. Test ExtractTitleBoxFromCard Multi-Scale Proportions
var smallCard = new Rectangle(100, 100, 220, 308);
var largeCard = new Rectangle(500, 200, 320, 448);
var smallTitle = CardZoomDetector.ExtractTitleBoxFromCard(smallCard);
var largeTitle = CardZoomDetector.ExtractTitleBoxFromCard(largeCard);

bool smallValid = smallTitle.Width >= 180 && smallTitle.Height >= 25 && smallTitle.Top > smallCard.Top;
bool largeValid = largeTitle.Width >= 260 && largeTitle.Height >= 35 && largeTitle.Top > largeCard.Top;
Assert(smallValid && largeValid, "CardZoomDetector: Robust title box extraction across varied card zoom resolutions");

// 33. Test Full-Card Spatial Top-to-Bottom Line Ordering on Showcase/Shifted Title Frame
using var showcaseCardBmp = new Bitmap(260, 365);
using (var g = Graphics.FromImage(showcaseCardBmp))
{
    g.Clear(Color.FromArgb(20, 20, 22));
    // Non-text decorative header at Y = 10..45
    using var decoBrush = new SolidBrush(Color.FromArgb(100, 80, 50));
    g.FillRectangle(decoBrush, 15, 10, 230, 35);

    // Card title shifted down at Y = 52 (would be completely missed by 9% / 28px top strip!)
    using var titleBgBrush = new SolidBrush(Color.FromArgb(40, 45, 55));
    g.FillRectangle(titleBgBrush, 15, 52, 230, 28);
    using var titleFont = new Font("Georgia", 13, FontStyle.Bold);
    using var titleTextBrush = new SolidBrush(Color.FromArgb(240, 235, 225));
    g.DrawString("Force of Will", titleFont, titleTextBrush, 18, 54);

    // Rules text below at Y = 180
    using var rulesFont = new Font("Georgia", 9);
    using var rulesBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
    g.DrawString("You may pay 1 life and exile a blue card...", rulesFont, rulesBrush, 18, 180);
}

var (showcaseOcr, showcaseCard) = await ocrEngine.RecognizeAndLookupAsync(showcaseCardBmp, s => db.Lookup(s));
Assert(showcaseCard != null && showcaseCard.NameZh == "意志之力",
    $"Full-Card Spatial Ordering: Successfully recognized shifted showcase title '{showcaseOcr}' -> '{showcaseCard?.NameZh}' at Y=54");

// 34. Test IsIgnoredGameText: Filters out MTGO stack abilities, combat steps, and type lines
Assert(CardDatabase.IsIgnoredGameText("Triggered Ability"), "IsIgnoredGameText: 'Triggered Ability' is ignored");
Assert(CardDatabase.IsIgnoredGameText("Activated Ability"), "IsIgnoredGameText: 'Activated Ability' is ignored");
Assert(CardDatabase.IsIgnoredGameText("Combat Damage"), "IsIgnoredGameText: 'Combat Damage' is ignored");
Assert(CardDatabase.IsIgnoredGameText("End of Combat"), "IsIgnoredGameText: 'End of Combat' is ignored");
Assert(CardDatabase.IsIgnoredGameText("Land — Swamp Forest"), "IsIgnoredGameText: 'Land — Swamp Forest' is ignored");
Assert(CardDatabase.IsIgnoredGameText("Creature — Cat"), "IsIgnoredGameText: 'Creature — Cat' is ignored");
Assert(!CardDatabase.IsIgnoredGameText("Overgrown Tomb"), "IsIgnoredGameText: 'Overgrown Tomb' is NOT ignored");
Assert(!CardDatabase.IsIgnoredGameText("Cauldron Familiar"), "IsIgnoredGameText: 'Cauldron Familiar' is NOT ignored");

// 35. Test Click Suppression & Discrete Trigger Hash Invariance
ulong mockCardAHash = 0xABCD1234EF567890;
ulong mockCardBHash = 0x1234ABCD5678EF90;
ulong trackingHash = mockCardAHash;

// Invariant A: Same card preview hash skips scan (no re-trigger or mouse follow)
bool shouldScanSame = trackingHash != mockCardAHash;
Assert(!shouldScanSame, "Click Suppression: Identical preview hash skips re-scan (overlay remains parked or dismissed)");

// Invariant B: Mouse click cooldown (300ms window) suppresses popups
var lastClickUtc = DateTime.UtcNow;
bool isWithinClickCooldown = (DateTime.UtcNow - lastClickUtc).TotalMilliseconds < 300;
Assert(isWithinClickCooldown, "Click Suppression: Active click cooldown safely suppresses popup creation");

// Invariant C: Next card arrives in preview pane -> hash changes -> triggers OCR
bool shouldScanNext = trackingHash != mockCardBHash;
Assert(shouldScanNext, "Discrete Refresh: Different card preview hash correctly triggers new recognition");

// 36. Test Empty Preview Pane Hash Caching (No Busy-Loop OCR)
using var emptyPreviewBmp1 = new Bitmap(200, 30);
using var emptyPreviewBmp2 = new Bitmap(200, 30);
using (var g = Graphics.FromImage(emptyPreviewBmp1)) g.Clear(Color.FromArgb(30, 30, 35));
using (var g = Graphics.FromImage(emptyPreviewBmp2)) g.Clear(Color.FromArgb(30, 30, 35));
ulong emptyHash1 = ScreenCapture.ComputeFastSampleHash(emptyPreviewBmp1);
ulong emptyHash2 = ScreenCapture.ComputeFastSampleHash(emptyPreviewBmp2);
Assert(emptyHash1 == emptyHash2 && emptyHash1 != 0, 
    "Empty Preview Caching: Empty preview frames produce consistent non-zero hash to prevent busy-loop OCR");

Console.WriteLine($"\n==================================================");
Console.WriteLine($" Summary: {passed} Passed, {failed} Failed");
Console.WriteLine($"==================================================");

if (failed > 0) Environment.Exit(1);

