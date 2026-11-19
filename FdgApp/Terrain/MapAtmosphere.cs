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
    /// <summary>Map mats that are not pictures (Kai, 2026-11-18): "" = the horizon's own ground under the play area too, so
    /// table and land are one surface (on the battlefield; a room's table shows felt); <see cref="GroundMat"/> = that ground
    /// everywhere; <see cref="FeltMat"/> = the plain felt. Anything else is a downloaded mat picture.</summary>
    public const string GroundMat = "ground", FeltMat = "felt";

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

    /// <summary>
    /// A map's light (Kai, 2026-11-18: "edit the light of the map"): the sun's height above the horizon and its turn round
    /// the table (degrees), brightness (1 = normal), warmth (-1 cold blue .. 0 neutral .. 1 warm orange) and how far the sky
    /// picture is turned. Saved as "h=58;t=32;b=1;w=0;s=0" (<see cref="ToText"/>); "" is the default.
    /// </summary>
    public sealed record Light(float SunHeight = 58f, float SunTurn = 32f, float Brightness = 1f, float Warmth = 0f, float SkyTurn = 0f)
    {
        public static readonly Light Default = new();

        public static Light Parse(string? text)
        {
            var l = Default;
            foreach (string part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] kv = part.Split('=', 2);
                if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
                l = kv[0] switch
                {
                    "h" => l with { SunHeight = Math.Clamp(v, 5f, 90f) },
                    "t" => l with { SunTurn = v },
                    "b" => l with { Brightness = Math.Clamp(v, 0.1f, 3f) },
                    "w" => l with { Warmth = Math.Clamp(v, -1f, 1f) },
                    "s" => l with { SkyTurn = v },
                    _ => l,
                };
            }
            return l;
        }

        /// <summary>The saved form; "" when everything is the default.</summary>
        public string ToText()
        {
            if (this == Default) return "";
            string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return $"h={F(SunHeight)};t={F(SunTurn)};b={F(Brightness)};w={F(Warmth)};s={F(SkyTurn)}";
        }
    }
}
