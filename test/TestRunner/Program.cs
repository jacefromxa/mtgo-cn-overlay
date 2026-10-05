using System;
using System.Diagnostics;
using System.Drawing;
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

// 2. Test Exact Match
var bolt = db.Lookup("Lightning Bolt");
Assert(bolt != null && bolt.NameZh == "闪电击", "Exact Match: 'Lightning Bolt' -> '闪电击'");
Assert(bolt?.NameEn == "Lightning Bolt", $"Title Casing: '{bolt?.NameEn}' == 'Lightning Bolt'");

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

// 8. Test AppConfig Load & Save
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
    using var testBmp = new Bitmap(280, 32);
    using (var g = Graphics.FromImage(testBmp))
    {
        g.Clear(Color.FromArgb(30, 30, 35));
        using var font = new Font("Georgia", 14, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        g.DrawString("Lightning Bolt", font, brush, 6, 4);
    }

    string recognized = await ocrEngine.RecognizeTextAsync(testBmp);
    Console.WriteLine($"[INFO] Synthesized OCR output: '{recognized}'");
    var recognizedCard = db.Lookup(recognized);
    Assert(recognizedCard != null && recognizedCard.NameZh == "闪电击", $"OCR to DB Lookup: '{recognized}' -> '闪电击'");
}

Console.WriteLine($"\n==================================================");
Console.WriteLine($" Summary: {passed} Passed, {failed} Failed");
Console.WriteLine($"==================================================");

if (failed > 0) Environment.Exit(1);
