using System.Text.Json.Serialization;

namespace MTGOOverlay.Models;

public class CardEntry
{
    [JsonPropertyName("n")]
    public string? NameZh { get; set; }

    [JsonPropertyName("en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("t")]
    public string? RulesText { get; set; }

    [JsonPropertyName("oracleText")]
    public string? OracleText { get; set; }

    [JsonPropertyName("y")]
    public string? TypeLine { get; set; }

    [JsonPropertyName("c")]
    public string? ManaCost { get; set; }

    [JsonPropertyName("p")]
    public string? Power { get; set; }

    [JsonPropertyName("q")]
    public string? Toughness { get; set; }

    [JsonPropertyName("l")]
    public string? Loyalty { get; set; }

    [JsonPropertyName("d")]
    public string? Defense { get; set; }

    [JsonPropertyName("f")]
    public string? FlavorText { get; set; }

    [JsonPropertyName("fn")]
    public string? FlavorName { get; set; }

    [JsonPropertyName("faces")]
    public List<CardEntry>? Faces { get; set; }

    [JsonPropertyName("_src")]
    public string Source { get; set; } = "Local";
}
