using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MTGOOverlay.Data;

public class OracleTextProvider
{
    private static readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MTGOOverlay",
        "oracle_cache.json");

    private static bool _initialized = false;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // Preseed common tournament staple cards for instant 0ms offline English oracle text
        _cache["Lightning Bolt"] = "Lightning Bolt deals 3 damage to any target.";
        _cache["Counterspell"] = "Counter target spell.";
        _cache["Dark Ritual"] = "Add {B}{B}{B}.";
        _cache["Thoughtseize"] = "Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life.";
        _cache["Fatal Push"] = "Destroy target creature if it has mana value 2 or less. Revolt — Destroy that creature if it has mana value 4 or less instead if a permanent you controlled left the battlefield this turn.";
        _cache["Brainstorm"] = "Draw three cards, then put two cards from your hand on top of your library in any order.";
        _cache["Swords to Plowshares"] = "Exile target creature. Its controller gains life equal to its power.";
        _cache["Wrath of God"] = "Destroy all creatures. They can't be regenerated.";
        _cache["Force of Will"] = "You may pay 1 life and exile a blue card from your hand rather than pay this spell's mana cost.\nCounter target spell.";
        _cache["Wasteland"] = "{T}: Add {C}.\n{T}, Sacrifice Wasteland: Destroy target nonbasic land.";
        _cache["Grief"] = "Menace\nWhen Grief enters the battlefield, target opponent reveals their hand. You choose a nonland card from it. That player discards that card.\nEvoke—Exile a black card from your hand.";
        _cache["Orcish Bowmasters"] = "Flash\nWhen Orcish Bowmasters enters the battlefield and whenever an opponent draws a card except the first one they draw in each of their draw steps, Orcish Bowmasters deals 1 damage to any target and you amass Orcs 1.";
        _cache["Murktide Regent"] = "Delve\nFlying\nMurktide Regent enters the battlefield with a +1/+1 counter on it for each instant and sorcery card exiled with it.\nWhenever an instant or sorcery card leaves your graveyard, put a +1/+1 counter on Murktide Regent.";
        _cache["Sol Ring"] = "{T}: Add {C}{C}.";
        _cache["Demonic Tutor"] = "Search your library for a card, put that card into your hand, then shuffle.";
        _cache["Black Lotus"] = "{T}, Sacrifice Black Lotus: Add three mana of any one color.";
        _cache["Time Walk"] = "Take an extra turn after this one.";
        _cache["Ancestral Recall"] = "Target player draws three cards.";
        _cache["Mox Pearl"] = "{T}: Add {W}.";
        _cache["Mox Sapphire"] = "{T}: Add {U}.";
        _cache["Mox Jet"] = "{T}: Add {B}.";
        _cache["Mox Ruby"] = "{T}: Add {R}.";
        _cache["Mox Emerald"] = "{T}: Add {G}.";
        _cache["Mana Vault"] = "Mana Vault doesn't untap during your untap step.\nAt the beginning of your upkeep, you may pay {4}. If you do, untap Mana Vault.\nAt the beginning of your draw step, if Mana Vault is tapped, it deals 1 damage to you.\n{T}: Add {C}{C}{C}.";
        _cache["Tarmogoyf"] = "Tarmogoyf's power is equal to the number of card types among cards in all graveyards and its toughness is equal to that number plus 1.";
        _cache["Liliana of the Veil"] = "+1: Each player discards a card.\n−2: Target player sacrifices a creature.\n−6: Separate all permanents target player controls into two piles. That player sacrifices all permanents in the pile of their choice.";
        _cache["Chalice of the Void"] = "Chalice of the Void enters the battlefield with X charge counters on it.\nWhenever a player casts a spell with mana value equal to the number of charge counters on Chalice of the Void, counter that spell.";
        _cache["Blood Moon"] = "Nonbasic lands are Mountains.";
        _cache["Ponder"] = "Look at the top three cards of your library, then put them back in any order. You may shuffle. Draw a card.";
        _cache["Preordain"] = "Scry 2, then draw a card.";
        _cache["Daze"] = "You may return an Island you control to its owner's hand rather than pay this spell's mana cost.\nCounter target spell unless its controller pays {1}.";
        _cache["Snapcaster Mage"] = "Flash\nWhen Snapcaster Mage enters the battlefield, target instant or sorcery card in your graveyard gains flashback until end of turn. The flashback cost is equal to its mana cost.";

        try
        {
            if (File.Exists(CacheFile))
            {
                string json = File.ReadAllText(CacheFile);
                var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _cache[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch { }

        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MTGOOverlay/1.0 (Windows)");
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public static string? GetOracleText(string cardName, Action<string, string>? onFetched = null)
    {
        Initialize();

        if (string.IsNullOrWhiteSpace(cardName)) return null;

        if (_cache.TryGetValue(cardName, out var cached))
        {
            return cached;
        }

        // Fetch asynchronously in background without blocking OCR
        _ = Task.Run(async () =>
        {
            try
            {
                string url = $"https://api.scryfall.com/cards/named?exact={Uri.EscapeDataString(cardName)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/json");
                using var resp = await _http.SendAsync(request);
                if (resp.IsSuccessStatusCode)
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    string? oracle = null;
                    if (doc.RootElement.TryGetProperty("oracle_text", out var ot))
                    {
                        oracle = ot.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("card_faces", out var faces) && faces.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var face in faces.EnumerateArray())
                        {
                            if (face.TryGetProperty("oracle_text", out var fot))
                            {
                                if (sb.Length > 0) sb.Append("\n//\n");
                                sb.Append(fot.GetString());
                            }
                        }
                        oracle = sb.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(oracle))
                    {
                        _cache[cardName] = oracle;
                        SaveCache();
                        onFetched?.Invoke(cardName, oracle);
                    }
                }
            }
            catch { }
        });

        return null;
    }

    private static void SaveCache()
    {
        try
        {
            string dir = Path.GetDirectoryName(CacheFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_cache);
            File.WriteAllText(CacheFile, json);
        }
        catch { }
    }
}
