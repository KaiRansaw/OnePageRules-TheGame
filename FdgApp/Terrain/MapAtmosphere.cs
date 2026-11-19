using FDG;

namespace FdgRaylib.Terrain;

/// <summary>
/// The look around and over a map (Kai, 2026-11-18): the horizon beyond the table and the weather over it. Cosmetic -
/// the playable table and its rules are unchanged. The names are saved in the map (TerrainLayoutFile.Horizon /
/// .Weather) and sent to every player with it.
/// </summary>
public static class MapAtmosphere
{
    /// <summary>One weather: its id (saved in the map) and the name shown.</summary>
    public sealed record Weather(string Id, string Name);

    public const string Clear = "";

    /// <summary>Every weather the game draws, in the order the Map Maker lists them.</summary>
    public static readonly IReadOnlyList<Weather> Weathers = new Weather[]
    {
        new(Clear, "Clear"),
        new("rain", "Rain"),
        new("heavy-rain", "Heavy rain"),
        new("storm", "Thunderstorm"),
        new("snow", "Snow"),
        new("blizzard", "Blizzard"),
        new("fog", "Fog"),
        new("ash", "Falling ash"),
        new("dust", "Dust storm"),
    };

    public static Weather? WeatherById(string? id) => Weathers.FirstOrDefault(w => w.Id == (id ?? ""));

    /// <summary>"Auto" (follow the map's generator theme) then the four generator themes - the Advanced Rulebooks'
    /// Generic, Hospitable, Wasteland and Death tables.</summary>
    public static readonly IReadOnlyList<string> Horizons =
        new[] { "" }.Concat(Enum.GetNames<TerrainBook.ETheme>()).ToArray();

    public static string HorizonName(string id) => id.Length == 0 ? "Auto (the map's terrain style)" : id switch
    {
        nameof(TerrainBook.ETheme.Hospitable) => "Hospitable - green, lived-in land",
        nameof(TerrainBook.ETheme.Wasteland) => "Wasteland - dry, wrecked land",
        nameof(TerrainBook.ETheme.Death) => "Death - a hostile world",
        _ => "Generic - plain open land",
    };

    /// <summary>The horizon a game draws: the map's choice, else its generator theme, else Generic.</summary>
    public static TerrainBook.ETheme HorizonFor(string? horizon) =>
        Enum.TryParse(horizon, out TerrainBook.ETheme t) ? t : TerrainBook.ETheme.Generic;
}
