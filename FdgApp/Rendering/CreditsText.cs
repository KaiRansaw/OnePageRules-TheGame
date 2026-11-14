namespace FdgRaylib.Rendering;

/// <summary>
/// The in-game Credits page (to-do, 2026-11-13): who made what the game uses. The game-icons.net icons are CC BY 3.0,
/// which requires the credit to be shown; the fonts' OFL and the libraries' MIT notices are reproduced in full in
/// THIRD-PARTY-NOTICES.txt and fonts/OFL-Rajdhani-Exo2.txt beside the game. ASCII only (CLAUDE.md).
/// </summary>
public static class CreditsText
{
    public sealed record Entry(string Name, string By, string Licence, string? Link = null);
    public sealed record Section(string Title, IReadOnlyList<Entry> Entries, string? Note = null);

    public static readonly IReadOnlyList<Section> Sections = new[]
    {
        new Section("The game", new[]
        {
            new Entry("OnePageRules - The Game", "Kai Ransaw - vision, design and testing", "Made for playing with friends"),
            new Entry("Future of Dark Grimness (the rules engine it grew from)", "Chris Orris", "MIT License"),
        }),
        new Section("The rules", new[]
        {
            new Entry("Grimdark Future, GF: Firefight, GF: Star Quest, Age of Fantasy, AoF: Skirmish, AoF: Quest",
                "OnePageRules (OPR Games) - created by Gaetano Ferrara", "Rules and army books (c) OPR Games", "https://www.onepagerules.com"),
            new Entry("Army books and rule wording", "Army Forge", "Imported from OPR's Army Forge", "https://army-forge.onepagerules.com"),
        }, "A fan-made program, not made or endorsed by OPR Games. Buy their books - they are what this game plays."),
        new Section("Icons", new[]
        {
            new Entry("Game icons", "Lorc, Delapouite and sbed - game-icons.net", "CC BY 3.0 (recoloured)", "https://game-icons.net"),
        }),
        new Section("Fonts", new[]
        {
            new Entry("Rajdhani", "Indian Type Foundry", "SIL Open Font License 1.1"),
            new Entry("Exo 2", "Natanael Gama", "SIL Open Font License 1.1"),
            new Entry("DejaVu Sans", "DejaVu fonts team", "Bitstream Vera / public domain"),
        }),
        new Section("Built with", new[]
        {
            new Entry("Godot Engine", "Juan Linietsky, Ariel Manzur and contributors", "MIT License", "https://godotengine.org"),
            new Entry(".NET", "Microsoft and the .NET Foundation", "MIT License"),
            new Entry("Newtonsoft.Json", "James Newton-King", "MIT License"),
            new Entry("System.Reactive (Rx.NET)", ".NET Foundation and contributors", "MIT License"),
            new Entry("Mono.Nat", "Alan McGovern, Ben Motmans, Nicholas Terry", "MIT License"),
            new Entry("Raylib-cs, raylib, rlImGui-cs, Dear ImGui, TinyDialogsNet (the older build)", "Their authors", "zlib / MIT"),
        }, "Full licence texts: THIRD-PARTY-NOTICES.txt beside the game."),
        new Section("Terrain models (shipped with the game)", new[]
        {
            new Entry("Nature, Castle, Fantasy Town, City (Industrial), Space and Survival kits", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl"),
            new Entry("Medieval Village MegaKit", "Quaternius", "CC0 1.0 (public domain)", "https://quaternius.com"),
        }, "Free to use without credit - credited anyway. The Nature Kit's colours are toned down in the game."),
        new Section("3D models (downloaded by you, never shipped)", new[]
        {
            new Entry("ForceOrg - Warhammer 40k models for Tabletop Simulator", "The ForceOrg mod's makers and contributors", "Community uploads, personal use"),
            new Entry("Stormvault - Age of Sigmar models for Tabletop Simulator", "The AoS-TTS team, photogrammetrists and modellers", "Community uploads, personal use", "https://github.com/AoS-TTS/Stormvault"),
        }, "Warhammer models are Games Workshop's. Matched to OPR units following onepagefan.wiki's Unit Equivalences."),
    };
}
