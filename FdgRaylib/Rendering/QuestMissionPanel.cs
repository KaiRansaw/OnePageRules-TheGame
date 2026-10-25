using System.Numerics;
using FDG;
using FDG.Stages;
using FDG.StateMachine;   // StarQuestMissionRules: the mission's name tables and thresholds.
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Star Quest mission window: what the party has to do, what the table is doing to them, and what the
/// letters on the markers mean. Open for the whole game.
///
/// <para>The objectives are rolled openly at set-up - the players need them to lay the table out - so
/// there is nothing to hide and no reason to make the player remember a banner that flashed once in round
/// one. Same for the weather, the event and the alert level: they change how every activation plays, so
/// they stay on screen instead of living in the log.</para>
///
/// <para>Drawn as an ImGui window so it can be moved, folded and scrolled; it remembers where you put it
/// between sessions like the other panels.</para>
/// </summary>
public static class QuestMissionPanel
{
    /// <summary>Whether the window is shown (toggled with M, or from the controls list).</summary>
    public static bool Open = true;

    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);
    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.70f, 0.35f, 1f);
    private static readonly Vector4 Body = new(0.88f, 0.88f, 0.86f, 1f);

    /// <summary>The marker letters, in the order they are listed. Kept here so the key and the table agree.</summary>
    private static readonly (string Mark, string Meaning, Vector4 Color)[] Legend =
    {
        ("?",  "Search token - stand within 1\" and use Skill > Discover", new Vector4(0.72f, 0.45f, 0.95f, 1f)),
        ("$",  "Treasure found - walk to it and Discover again to pick it up", new Vector4(1f, 0.84f, 0.35f, 1f)),
        ("!",  "Objective marker - Skill > Objective while within 1\"", new Vector4(0.45f, 0.78f, 1f, 1f)),
        ("D",  "Delivery point", new Vector4(0.45f, 0.78f, 1f, 1f)),
        ("X",  "Extraction point", new Vector4(0.45f, 0.78f, 1f, 1f)),
        ("F",  "Fire - within 3\" is dangerous terrain", new Vector4(0.94f, 0.35f, 0.16f, 1f)),
        ("S",  "Smoke - within 3\" counts as cover", new Vector4(0.67f, 0.67f, 0.67f, 1f)),
        ("G",  "Grease - within 3\" is difficult terrain", new Vector4(0.62f, 0.55f, 0.35f, 1f)),
        ("N",  "Noise - enemies head for it", new Vector4(0.78f, 0.47f, 0.90f, 1f)),
        ("P",  "Phantasm - enemies treat it as a hero", new Vector4(0.78f, 0.47f, 0.90f, 1f)),
        ("Po", "Poisoned - on activating, a 4+ costs it a wound", new Vector4(0.43f, 0.78f, 0.27f, 1f)),
        ("Si", "Silenced - -2 to casting this round", new Vector4(0.78f, 0.47f, 0.90f, 1f)),
    };

    /// <summary>Draws the window. Does nothing outside a Star Quest game.</summary>
    public static void Draw(ITableState? table, int screenW, int screenH)
    {
        if (!Open || table?.Progress.Quest is not { } q) return;

        ImGui.SetNextWindowSize(new Vector2(screenW * 0.21f, screenH * 0.34f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(14f, screenH * 0.30f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.93f);
        bool open = Open;
        if (!ImGui.Begin("Mission", ref open, ImGuiWindowFlags.NoFocusOnAppearing))
        {
            Open = open;
            ImGui.End();
            return;
        }
        Open = open;

        ImGui.PushTextWrapPos(0f);

        // ── What the party has to do ──
        if (q.Primary > 0)
        {
            UiText.Colored(Gold, "OBJECTIVES");
            string primary = StarQuestMissionRules.PrimaryNames[Math.Clamp(q.Primary, 0, StarQuestMissionRules.PrimaryNames.Length - 1)];
            UiText.Colored(q.PrimaryDone ? Good : Body, $"Primary: {primary}{(q.PrimaryDone ? " - done" : "")}");
            if (q.Secondary > 0)
            {
                string secondary = StarQuestMissionRules.SecondaryNames[Math.Clamp(q.Secondary, 0, StarQuestMissionRules.SecondaryNames.Length - 1)];
                UiText.Colored(q.SecondaryDone ? Good : Body, $"Secondary: {secondary}{(q.SecondaryDone ? " - done" : "")}");
            }
            string hint = QuestMission.ObjectiveHint(q);
            if (hint.Length > 0) UiText.Disabled(hint);
            ImGui.Spacing();
        }

        // ── What the table is doing ──
        UiText.Colored(Gold, "CONDITIONS");
        bool any = false;
        any |= Row("Wave", q.Wave > 0 && q.Difficulty == 0 ? q.Wave.ToString() : "");
        any |= Row("Difficulty", q.Difficulty > 0
            ? StarQuestMissionRules.DifficultyNames[Math.Clamp(q.Difficulty, 0, StarQuestMissionRules.DifficultyNames.Length - 1)] : "");
        any |= Row("Alertness", q.Alertness > 0 ? $"{q.Alertness} / 6" : "");
        any |= Row("Alert level", q.AlertLevel);
        any |= Row("Time of day", q.TimeOfDay);
        any |= Row("Weather", q.Weather);
        if (q.Event.Length > 0)
        {
            ImGui.Spacing();
            UiText.Colored(Warn, "Event: " + q.Event);
            any = true;
        }
        if (q.Challenges.Count > 0)
        {
            UiText.Disabled("Challenges: " + string.Join(", ", q.Challenges));
            any = true;
        }
        if (!any) UiText.Disabled("Nothing in play.");

        // ── What the letters mean ──
        ImGui.Spacing();
        if (ImGui.TreeNodeEx("Marker key", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            foreach (var (mark, meaning, color) in Legend)
            {
                UiText.Colored(color, mark);
                ImGui.SameLine(ImGui.GetFontSize() * 2.2f);
                UiText.Disabled(meaning);
            }
            ImGui.TreePop();
        }

        ImGui.PopTextWrapPos();
        ImGui.End();
    }

    /// <summary>"Label: value" on one line, skipped when there is no value. True when it drew something.</summary>
    private static bool Row(string label, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        // UiText, not TextColored: the value is data and may carry a '%' (#398).
        UiText.Colored(Dim, label + ": ");
        ImGui.SameLine(0f, 0f);
        UiText.Colored(Body, value);
        return true;
    }
}
