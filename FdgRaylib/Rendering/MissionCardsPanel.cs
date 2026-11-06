using System.Numerics;
using FDG;
using FDG.Missions;
using FDG.Players;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Mission Cards in the Mission tab: victory points, the cards on the table (hover one for what it
/// asks), and what has been scored. Plain on purpose - layout code, rented until the Godot port.
/// </summary>
internal static class MissionCardsPanel
{
    private static readonly Vector4 Heading = new(0.55f, 0.85f, 1f, 1f);

    public static void Draw(MissionCardState state, EGameSystem system, IReadOnlyList<IPlayerSlotInfo> seats,
        Func<PlayerID, Color> colorFor)
    {
        ImGui.Spacing();
        ImGui.Separator();
        UiText.Colored(Heading, $"MISSION CARDS - {MissionCardCatalog.Name(state.Game).ToUpperInvariant()}");

        ImGui.TextDisabled("Victory points:");
        foreach (IPlayerSlotInfo seat in seats)
        {
            int vp = state.Vp.TryGetValue(seat.SlotID, out int v) ? v : 0;
            UiText.Colored(ToVec4(colorFor(seat.PlayerID)), "  " + seat.Name);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{vp} VP");
        }

        if (state.Game == EMissionCardGame.BattleOfWits)
        {
            foreach (IPlayerSlotInfo seat in seats)
            {
                if (!state.Sides.TryGetValue(seat.SlotID, out MissionCardSide? side)) continue;
                ImGui.Spacing();
                UiText.Colored(ToVec4(colorFor(seat.PlayerID)), $"{seat.Name}'s cards:");
                DrawCards(state, system, side.Table, seat.SlotID);
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Shared cards:");
            DrawCards(state, system, state.SharedTable, -1);
        }

        List<MissionScore> scored = state.Scored;
        if (scored.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Scored:");
            foreach (MissionScore s in scored)
            {
                IPlayerSlotInfo? seat = seats.FirstOrDefault(p => p.SlotID == s.Slot);
                string who = seat?.Name ?? "?";
                MissionCardDef def = MissionCardCatalog.Card(system, s.Card);
                UiText.Wrapped($"  R{s.Round} {who}: {def.Title} +{s.Vp} VP");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(s.Text);
            }
        }
        ImGui.TextDisabled("End of game: +1 VP per objective you control.");
    }

    private static void DrawCards(MissionCardState state, EGameSystem system, List<int> table, int prioritySlot)
    {
        if (table.Count == 0)
        {
            ImGui.TextDisabled("  (none)");
            return;
        }
        foreach (int number in table)
        {
            MissionCardDef def = MissionCardCatalog.Card(system, number);
            int top = def.Conditions.Max(c => c.Vp);
            UiText.Wrapped($"  [{number}] {def.Title} - up to {top} VP");
            if (!ImGui.IsItemHovered()) continue;

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(360f);
            UiText.Colored(Heading, $"{number} {def.Title}");
            foreach (MissionCondition c in def.Conditions)
                ImGui.TextUnformatted($"{c.Vp} VP: {c.Text}");
            if (number == 44 && state.Priority.TryGetValue(MissionCardState.PriorityKey(prioritySlot, 44), out int obj))
                UiText.Colored(Heading, $"Rolled: objective {obj}.");
            if (def.Note != null) ImGui.TextDisabled(def.Note);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static Vector4 ToVec4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}
