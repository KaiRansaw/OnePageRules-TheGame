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
        new Section("Rooms around the table (downloaded on your computer)", new[]
        {
            new Entry("360-degree room photos", "Poly Haven and its photographers", "CC0 - public domain", "https://polyhaven.com"),
        }),
        new Section("Battlefield and weather", new[]
        {
            new Entry("Battlefield sky and ground (DaySkyHDRI071B, Ground037)", "ambientCG (Lennart Demes)", "CC0 1.0 (public domain)", "https://ambientcg.com"),
            new Entry("Generic, Wasteland and Death grounds (Ground067, Ground031, Ground054, Rock033, Ground051)", "ambientCG (Lennart Demes)", "CC0 1.0 (public domain)", "https://ambientcg.com"),
            new Entry("Generic and Wasteland skies (Autumn Field, Kloppenheim 06 - Pure Sky)", "Poly Haven", "CC0 1.0 (public domain)", "https://polyhaven.com"),
            new Entry("Terrain surfaces - lava, mud, soil, grass (Lava004, Lava001, Ground048, Ground068, Ground003, Grass004, Grass002)", "ambientCG (Lennart Demes)", "CC0 1.0 (public domain)", "https://ambientcg.com"),
            new Entry("Water ripples (Water 001)", "3DTextures.me", "CC0 1.0 (public domain)", "https://3dtextures.me"),
            new Entry("Weather particle pictures (Particle Pack)", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl/assets/particle-pack"),
            new Entry("Rain set-up based on TCA Weather System", "kS222", "MIT", "https://github.com/kS222138/TCA_Weather_System"),
        }),
        new Section("Battle effects and sounds", new[]
        {
            new Entry("Muzzle flashes, swings and hit effects (Muzzle Flash VFX, Battle FX, Hit FX)", "Binbun3D", "CC0 1.0 (public domain)", "https://binbun3d.itch.io"),
            new Entry("Battle sounds (150 game effect sounds)", "Various authors - see sounds/LICENSES-150-game-effect-sounds.txt", "CC0 1.0 (public domain)"),
            new Entry("Gun sounds", "alienanthony", "Public domain", "https://alienanthony.itch.io/gun-sounds"),
            new Entry("Sci-Fi Weapon Shots SFX", "Lentikula", "CC0 1.0 (public domain)", "https://lentikula.itch.io/sci-fi-weapon-shots-sfx-freecc0"),
        }),
        new Section("Music and game sounds", new[]
        {
            new Entry("Menu music (\"Space Cadet\", \"Flowing Rocks\" - Music Loops)", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl/assets/music-loops"),
            new Entry("Round, victory and defeat cues (Music Jingles, Music Loops idents)", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl/assets/music-jingles"),
            new Entry("Dice (Casino Audio)", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl/assets/casino-audio"),
            new Entry("Objective, morale and unit-destroyed cues (Interface Sounds, Impact Sounds)", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl"),
            new Entry("Distant rumbles in the battle ambience (Sci-Fi Sounds)", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl/assets/sci-fi-sounds"),
            new Entry("Wind and rain ambience", "Made for this game (generated sound)", "CC0 1.0 (public domain)"),
        }),
        new Section("Icons", new[]
        {
            new Entry("Game icons", "Lorc, Delapouite and sbed - game-icons.net", "CC BY 3.0 (recoloured)", "https://game-icons.net"),
            new Entry("Hologram UI frames", "Wenrexa - Free UI Hologram Interface", "CC0 - public domain", "https://opengameart.org/content/free-ui-hologram-interface"),
            new Entry("Sci-Fi Minimalism UI", "Wenrexa - UI Sci-Fi Minimalism #01", "CC0 - public domain", "https://wenrexa.itch.io/sci-fi-style-interfacenesia-01"),
        }),
        new Section("Fonts", new[]
        {
            new Entry("Rajdhani", "Indian Type Foundry", "SIL Open Font License 1.1"),
            new Entry("Exo 2", "Natanael Gama", "SIL Open Font License 1.1"),
            new Entry("Cinzel", "Natanael Gama (The Cinzel Project Authors)", "SIL Open Font License 1.1"),
            new Entry("Alegreya Sans", "Juan Pablo del Peral, Huerta Tipografica", "SIL Open Font License 1.1"),
            new Entry("DejaVu Sans", "DejaVu fonts team", "Bitstream Vera / public domain"),
        }),
        new Section("Built with", new[]
        {
            new Entry("Godot Engine", "Juan Linietsky, Ariel Manzur and contributors", "MIT License", "https://godotengine.org"),
            new Entry(".NET", "Microsoft and the .NET Foundation", "MIT License"),
            new Entry("Newtonsoft.Json", "James Newton-King", "MIT License"),
            new Entry("System.Reactive (Rx.NET)", ".NET Foundation and contributors", "MIT License"),
            new Entry("Mono.Nat", "Alan McGovern, Ben Motmans, Nicholas Terry", "MIT License"),
            new Entry("PDFium (reads paper-model PDF sheets)", "The PDFium Authors (Google)", "BSD 3-Clause / Apache 2.0", "https://pdfium.googlesource.com/pdfium/"),
            new Entry("Docnet.Core", "Modestas Petravicius", "MIT License"),
            new Entry("StbImageSharp", "StbSharp team, after Sean Barrett's stb_image", "Public domain"),
            new Entry("Raylib-cs, raylib, rlImGui-cs, Dear ImGui, TinyDialogsNet (the older build)", "Their authors", "zlib / MIT"),
        }, "Full licence texts: THIRD-PARTY-NOTICES.txt beside the game."),
        new Section("Terrain models (shipped with the game)", new[]
        {
            new Entry("Nature, Castle, Fantasy Town, City (Industrial), Space and Survival kits", "Kenney", "CC0 1.0 (public domain)", "https://kenney.nl"),
            new Entry("Stylized Nature, Medieval Village, Fantasy Props and Modular Sci-Fi MegaKits (terrain)", "Quaternius", "CC0 1.0 (public domain)", "https://quaternius.com"),
            new Entry("LowPoly Apocalyptic Buildings (terrain)", "Majadroid (Maik Hoffmann); textures from cc0textures.com", "CC0 1.0 (public domain)", "https://opengameart.org/content/3d-apocalyptic-building-city-cc0"),
            new Entry("Realistic terrain models (rocks, trees, plants, props, fort)", "Poly Haven", "CC0 1.0 (public domain)", "https://polyhaven.com"),
            new Entry("Medieval Village MegaKit", "Quaternius", "CC0 1.0 (public domain)", "https://quaternius.com"),
            new Entry("Medieval table (the tabletop and side tables)", "zeein4004", "CGTrader Royalty Free License - free for personal and commercial use", "https://www.cgtrader.com/free-3d-models/interior/other/medieval-table-d831b362-cf46-44a6-a213-9569976e38c3"),
        }, "Free to use without credit - credited anyway. The Nature Kit's colours are toned down in the game."),
        new Section("3D models (downloaded by you, never shipped)", new[]
        {
            new Entry("ForceOrg - Warhammer 40k models for Tabletop Simulator", "The ForceOrg mod's makers and contributors", "Community uploads, personal use"),
            new Entry("Stormvault - Age of Sigmar models for Tabletop Simulator", "The AoS-TTS team, photogrammetrists and modellers", "Community uploads, personal use", "https://github.com/AoS-TTS/Stormvault"),
        }, "Warhammer models are Games Workshop's. Matched to OPR units following onepagefan.wiki's Unit Equivalences."),
    };
}
