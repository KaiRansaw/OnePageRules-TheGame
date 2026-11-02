using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// What a Quest system's tile opens: Build Heroes, Play a Mission, Horde Mode, Campaign.
///
/// <para>
/// <b>A placeholder, and deliberately so.</b> Kai, 2026-11-02, on the two Quest tiles: <i>"when you
/// click on 5/6, they will open to their own screens which ill work on later."</i> So the design of this
/// screen is his and is not settled. What this is for is making sure nothing becomes unreachable in the
/// meantime: the main menu's Star Quest Heroes / AoF Quest Heroes / Campaign buttons came off in the
/// same pass, and without a door here the Hero Builder would simply have no way in. A feature that
/// quietly stops being reachable is the same as a feature deleted.
/// </para>
///
/// <para>
/// The two Quest systems are NOT the four campaign systems in miniature, which is why they do not share
/// <see cref="ModeSelectScreen"/>: their vocabulary is different (missions, not armies), they need a
/// hero-building door the others have no equivalent of, and their campaign is a different book. Sharing
/// one screen between them would be the exact mistake CLAUDE.md warns about.
/// </para>
/// </summary>
public sealed class QuestLandingScreen : IAppScreen
{
    public Action<EGameSystem>? OnHeroes;
    public Action<EGameSystem>? OnCampaign;
    public Action<EGameSystem>? OnMission;
    public Action<EGameSystem>? OnHorde;
    public Action? OnBack;

    public EGameSystem System { get; private set; } = EGameSystem.GFStarQuest;

    public void Open(EGameSystem system) => System = system;

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##questlanding",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuFont);

        string title = GameSystemInfo.DisplayName(System);
        Vector2 size = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2((screenW - size.X) * 0.5f, screenH * 0.10f));
        ImGui.TextUnformatted(title);

        float margin = screenW * 0.06f;
        float tileW = (screenW - margin * 2f - 60f) / 4f;
        float tileH = screenH * 0.26f;
        float top = screenH * 0.30f;

        Tile(0, "Build Heroes", "Make and level the party you play with.", () => OnHeroes?.Invoke(System));
        Tile(1, "Play a Mission", "One mission from the Campaign Builder.", () => OnMission?.Invoke(System));
        Tile(2, "Horde Mode", "Hold out against waves until the party falls.", () => OnHorde?.Invoke(System));
        Tile(3, "Campaign", "Chapters of missions, carried between them.", () => OnCampaign?.Invoke(System));

        ImGui.SetCursorPos(new Vector2(margin, screenH - screenH * 0.12f));
        if (UiButton.Back("Back", new Vector2(180f, 46f)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            OnBack?.Invoke();

        if (useMenuFont) ImGui.PopFont();
        ImGui.End();
        ImGui.PopStyleColor();

        void Tile(int index, string label, string blurb, Action go)
        {
            float x = margin + index * (tileW + 20f);
            ImGui.SetCursorPos(new Vector2(x, top));
            if (UiButton.Navigate(label + $"##quest{index}", new Vector2(tileW, tileH))) go();
            ImGui.SetCursorPos(new Vector2(x + 8f, top + tileH + 8f));
            ImGui.PushTextWrapPos(x + tileW);
            UiText.Colored(Dim, blurb);
            ImGui.PopTextWrapPos();
        }
    }
}
