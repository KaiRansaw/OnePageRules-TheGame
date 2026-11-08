using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The main menu. Kai, 2026-11-07 ("Main Menu Mockup", Option A): two big tiles, Grimdark Future and Age
/// of Fantasy, each opening the game screen with its three games side by side; the tools along the
/// bottom (Load Game, Join a Game, Army Forge, Map Maker, Settings, Close Game). The Army Builder and the
/// Combat Calculator are deliberately not on it.
/// </summary>
public class MainMenuScreen : IAppScreen
{
    /// <summary>A family tile was clicked.</summary>
    public Action<GameScreen.EFamily>? OnFamilyClicked;

    public Action? OnClientClicked;
    public Action? OnArmyForgeClicked;
    public Action? OnLoadGameClicked;
    public Action? OnMapMakerClicked;
    public Action? OnSettingsClicked;
    public Action? OnQuitClicked;

    /// <summary>The menu's title (Kai's wording, upper case as on the mockup).</summary>
    public const string Title = "ONEPAGERULES - THE GAME";

    /// <summary>The line under each tile's name.</summary>
    private static string Blurb(GameScreen.EFamily family) => family == GameScreen.EFamily.GrimdarkFuture
        ? "Grimdark Future - GF: Firefight - GF: Star Quest"
        : "Age of Fantasy - AoF: Skirmish - AoF: Quest";

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);

        ImGui.Begin("##MainMenu",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        // Prefer the large baked menu font and scale DOWN from it (crisp); fall back to stretching the
        // body font if the font atlas didn't load.
        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuFont);
        ImFontPtr font = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float basePx = useMenuFont ? RaylibRenderer.MenuFontPx : ImGui.GetFontSize();

        // ── Title ──────────────────────────────────────────────────────────────────────────────────
        float titlePx = screenH * 0.058f;
        ImGui.SetWindowFontScale(titlePx / basePx);
        const string title = Title;
        Vector2 titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2((screenW - titleSize.X) * 0.5f, screenH * 0.050f));
        ImGui.Text(title);
        ImGui.SetWindowFontScale(1.0f);


        // ── The two family tiles ─────────────────────────────────────────────────────────────────
        float marginX = screenW * 0.10f;
        float gapX    = screenW * 0.03f;
        float tileW   = (screenW - (marginX * 2f) - gapX) / 2f;
        float tileH   = screenH * 0.42f;
        float gridTop = screenH * 0.17f;

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 winPos = ImGui.GetWindowPos();

        GameScreen.EFamily[] families = { GameScreen.EFamily.GrimdarkFuture, GameScreen.EFamily.AgeOfFantasy };
        for (int i = 0; i < families.Length; i++)
        {
            GameScreen.EFamily family = families[i];
            Vector2 pos = new(marginX + (i * (tileW + gapX)), gridTop);

            ImGui.SetCursorPos(pos);
            // An id-only label: the tile's text is painted below, so the name and the blurb can wear
            // different sizes rather than sharing the button's one font.
            if (UiButton.Navigate($"##family{i}", new Vector2(tileW, tileH)))
                OnFamilyClicked?.Invoke(family);
            if (ImGui.IsItemHovered())
                UiText.TooltipWrapped($"Pick a game and a mode: {Blurb(family)}.");

            Vector2 tl = winPos + pos;
            float padX = tileW * 0.05f;
            float namePx  = screenH * 0.045f;
            float blurbPx = screenH * 0.021f;
            // Name and blurb sit on the tile's lower edge; the room above is kept for faction art.
            float blurbY = tileH - (tileH * 0.10f) - blurbPx;
            float nameY  = blurbY - namePx - (tileH * 0.02f);
            dl.AddText(font, namePx, tl + new Vector2(padX, nameY),
                ImGui.GetColorU32(new Vector4(0.93f, 0.93f, 0.93f, 1f)), GameScreen.FamilyName(family));
            dl.AddText(font, blurbPx, tl + new Vector2(padX, blurbY),
                ImGui.GetColorU32(new Vector4(0.70f, 0.70f, 0.72f, 1f)), Blurb(family));
        }

        // ── Tools along the bottom: one row of six ───────────────────────────────────────────────
        float btnH    = screenH * 0.065f;
        float btnGapX = screenW * 0.012f;
        float btnW    = (screenW - (marginX * 2f) - (btnGapX * 5f)) / 6f;
        float toolsTop = gridTop + tileH + (screenH * 0.07f);
        float btnPx   = screenH * 0.021f;

        void Tool(string label, Action? action, int index, string tip, bool back = false)
        {
            ImGui.SetCursorPos(new Vector2(marginX + (index * (btnW + btnGapX)), toolsTop));
            ImGui.SetWindowFontScale(btnPx / basePx);
            bool clicked = back
                ? UiButton.BackInList(label, new Vector2(btnW, btnH))
                : UiButton.Navigate(label, new Vector2(btnW, btnH));
            ImGui.SetWindowFontScale(1.0f);
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped(tip);
            if (clicked) action?.Invoke();
        }

        Tool("Load Game",   OnLoadGameClicked,  0, "Carry on with a game you saved.");
        Tool("Join a Game", OnClientClicked,    1, "Join a friend's game over Tailscale.");
        Tool("Army Forge",  OnArmyForgeClicked, 2, "Build and save army lists from the official books.");
        Tool("Map Maker",   OnMapMakerClicked,  3, "Make and edit your own maps.");
        Tool("Settings",    OnSettingsClicked,  4, "Your name, display, gameplay, camera, sound and the key list.");
        Tool("Close Game",  OnQuitClicked,      5, "Quit to the desktop.", back: true);

        if (useMenuFont) ImGui.PopFont();

        // Fan-project disclaimer, small but legible, centered along the bottom edge.
        const string disclaimer =
            "This game is a fan-made project and is not an official product of OnePageRules.";
        Vector2 discSize = ImGui.CalcTextSize(disclaimer);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.62f, 0.62f, 1f));
        ImGui.SetCursorPos(new Vector2((screenW - discSize.X) * 0.5f, screenH - discSize.Y - (screenH * 0.02f)));
        ImGui.TextUnformatted(disclaimer);
        ImGui.PopStyleColor();

        // Build stamp, bottom right: confirms at a glance that a pull actually landed before you
        // start a game. The commit beside the number updates itself when the number is forgotten.
        string build = AppVersion.BuildLabel;
        Vector2 buildSize = ImGui.CalcTextSize(build);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.50f, 0.50f, 0.50f, 1f));
        ImGui.SetCursorPos(new Vector2(
            screenW - buildSize.X - (screenW * 0.012f),
            screenH - buildSize.Y - (screenH * 0.012f)));
        ImGui.TextUnformatted(build);
        ImGui.PopStyleColor();

        ImGui.End();
        ImGui.PopStyleColor();
    }
}
