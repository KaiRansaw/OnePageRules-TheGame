using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The main menu. The Game Type is chosen HERE, on a grid of tiles, rather than inside the lobby: the
/// first question a player answers is which OPR game they are sitting down to, and answering it on the
/// way in means the lobby opens already pointed at that game's armies, points and modes.
///
/// <para>A tile raises <see cref="OnGameTypeClicked"/> and nothing else; Program.cs remembers the pick
/// and applies it to the lobby view model once the host dialog has created one. The lobby keeps its own
/// Game Type combo, pre-set to the tile that was clicked - that combo is for now also the "change your
/// mind" affordance.</para>
///
/// <para>The tool screens (Army Forge, the hero/campaign builders, the calculator, Load Game) sit below
/// the grid as a plain two-row strip: they are errands, not the thing the player came to do.</para>
/// </summary>
public class MainMenuScreen : IAppScreen
{
    /// <summary>A Game Type tile was clicked. Only available systems can raise this.</summary>
    public Action<EGameSystem>? OnGameTypeClicked;

    // Still raised by the campaign screens, which route through the host dialog on their own terms;
    // the menu itself no longer has a plain "Play" button.
    public Action? OnHostClicked;
    public Action? OnClientClicked;
    public Action? OnArmyBuilderClicked;
    public Action? OnArmyForgeClicked;
    public Action? OnCombatCalculatorClicked;
    public Action? OnLoadGameClicked;
    public Action? OnHeroesClicked;
    public Action? OnCampaignsClicked;
    public Action? OnStrikeTeamsClicked;
    public Action? OnQuitClicked;

    private bool _modalOpen = false;

    public void SetModalOpen(bool open) => _modalOpen = open;

    /// <summary>One line under each Game Type's name, saying what kind of game it is.</summary>
    private static string Blurb(EGameSystem system) => system switch
    {
        EGameSystem.GrimdarkFuture => "Full-scale sci-fi battles",
        EGameSystem.AgeOfFantasy   => "Full-scale fantasy battles",
        EGameSystem.GFFirefight    => "Small-squad skirmish",
        EGameSystem.AoFSkirmish    => "Small-warband skirmish",
        EGameSystem.GFStarQuest    => "Heroes vs enemy forces",
        EGameSystem.AoFQuest       => "Heroes vs enemy forces",
        _ => "",
    };

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
        const string title = "FUTURE of DARK GRIMNESS";
        Vector2 titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2((screenW - titleSize.X) * 0.5f, screenH * 0.050f));
        ImGui.Text(title);
        ImGui.SetWindowFontScale(1.0f);

        ImGui.BeginDisabled(_modalOpen);

        // ── Game Type tiles ────────────────────────────────────────────────────────────────────────
        float marginX = screenW * 0.075f;
        float gapX    = screenW * 0.018f;
        float tileW   = (screenW - (marginX * 2f) - (gapX * 2f)) / 3f;
        float tileH   = screenH * 0.185f;
        float gapY    = screenH * 0.022f;
        float gridTop = screenH * 0.165f;

        float capPx = screenH * 0.019f;
        ImGui.SetWindowFontScale(capPx / basePx);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.HeaderAccent);
        ImGui.SetCursorPos(new Vector2(marginX, gridTop - (screenH * 0.035f)));
        ImGui.TextUnformatted("CHOOSE A GAME TYPE");
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1.0f);

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 winPos = ImGui.GetWindowPos();

        EGameSystem[] order = GameSystemInfo.DisplayOrder;
        for (int i = 0; i < order.Length; i++)
        {
            EGameSystem system = order[i];
            bool available = GameSystemInfo.IsAvailable(system);

            int col = i % 3, row = i / 3;
            Vector2 pos = new(marginX + (col * (tileW + gapX)), gridTop + (row * (tileH + gapY)));

            ImGui.SetCursorPos(pos);
            ImGui.BeginDisabled(!available);
            // An id-only label: the tile's text is painted below, so the name and the blurb can wear
            // different sizes rather than sharing the button's one font.
            if (UiButton.Navigate($"##gametype{i}", new Vector2(tileW, tileH)) && available)
                OnGameTypeClicked?.Invoke(system);
            ImGui.EndDisabled();

            // Paint the tile's text over the button we just placed. Screen-space, because the draw
            // list works in screen coordinates while the cursor above is window-relative.
            Vector2 tl = winPos + pos;
            float padX = tileW * 0.065f;
            uint nameCol = ImGui.GetColorU32(available
                ? new Vector4(0.93f, 0.93f, 0.93f, 1f)
                : new Vector4(0.48f, 0.48f, 0.48f, 1f));
            uint blurbCol = ImGui.GetColorU32(available
                ? new Vector4(0.70f, 0.70f, 0.72f, 1f)
                : new Vector4(0.40f, 0.40f, 0.40f, 1f));

            float namePx  = screenH * 0.030f;
            float blurbPx = screenH * 0.019f;
            // Name and blurb sit on the tile's lower edge, the way the mockup's cards do - the empty
            // room above is where a faction image goes once per-type art exists.
            float blurbY = tileH - (tileH * 0.16f) - blurbPx;
            float nameY  = blurbY - namePx - (tileH * 0.03f);

            dl.AddText(font, namePx, tl + new Vector2(padX, nameY), nameCol,
                GameSystemInfo.DisplayName(system));
            dl.AddText(font, blurbPx, tl + new Vector2(padX, blurbY), blurbCol,
                available ? Blurb(system) : "Coming soon");
        }

        // ── Tool screens ───────────────────────────────────────────────────────────────────────────
        // Two rows of four, left-aligned under the grid, so the long campaign labels have room.
        float btnH    = screenH * 0.062f;
        float btnGapX = screenW * 0.014f;
        float btnGapY = screenH * 0.018f;
        float btnW    = (screenW - (marginX * 2f) - (btnGapX * 3f)) / 4f;
        float toolsTop = gridTop + (tileH * 2f) + gapY + (screenH * 0.055f);
        float btnPx   = screenH * 0.022f;

        void Tool(string label, Action? action, int index, bool back = false)
        {
            int col = index % 4, row = index / 4;
            ImGui.SetCursorPos(new Vector2(marginX + (col * (btnW + btnGapX)),
                                           toolsTop + (row * (btnH + btnGapY))));
            ImGui.SetWindowFontScale(btnPx / basePx);
            bool clicked = back
                ? UiButton.BackInList(label, new Vector2(btnW, btnH))
                : UiButton.Navigate(label, new Vector2(btnW, btnH));
            ImGui.SetWindowFontScale(1.0f);
            if (clicked) action?.Invoke();
        }

        Tool("Load Game",           OnLoadGameClicked,         0);
        Tool("Army Forge",          OnArmyForgeClicked,        1);
        Tool("Star Quest Heroes",   OnHeroesClicked,           2);
        Tool("Star Quest Campaign", OnCampaignsClicked,        3);
        Tool("Firefight Campaign",  OnStrikeTeamsClicked,      4);
        Tool("Combat Calculator",   OnCombatCalculatorClicked, 5);
        Tool("Quit",                OnQuitClicked,             6, back: true);

        ImGui.EndDisabled();

        if (useMenuFont) ImGui.PopFont();

        // Fan-project disclaimer, small but legible, centered along the bottom edge.
        const string disclaimer =
            "This game is a fan-made project and is not an official product of OnePageRules.";
        Vector2 discSize = ImGui.CalcTextSize(disclaimer);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.62f, 0.62f, 1f));
        ImGui.SetCursorPos(new Vector2((screenW - discSize.X) * 0.5f, screenH - discSize.Y - (screenH * 0.02f)));
        ImGui.TextUnformatted(disclaimer);
        ImGui.PopStyleColor();

        ImGui.End();
        ImGui.PopStyleColor();
    }
}
