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
        Func<PlayerID, Color> colorFor, IReadOnlyCollection<PlayerID>? localPlayers = null)
    {
        ImGui.Spacing();
        ImGui.Separator();
        UiText.Colored(Heading, $"MISSION CARDS - {MissionCardCatalog.Name(state.Game).ToUpperInvariant()}");

        UiText.Disabled("Victory points:");
        foreach (IPlayerSlotInfo seat in seats)
        {
            int vp = state.Vp.TryGetValue(seat.SlotID, out int v) ? v : 0;
            UiText.Colored(ToVec4(colorFor(seat.PlayerID)), "  " + seat.Name);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{vp} VP");
        }

        if (MissionCardCatalog.IsCustom(state.Game))
        {
            foreach (IPlayerSlotInfo seat in seats)
            {
                if (!state.Sides.TryGetValue(seat.SlotID, out MissionCardSide? side)) continue;
                // On a shared screen (hot seat, or nobody local) everything is shown; otherwise another
                // player's hand and face-down cards stay hidden.
                bool mine = localPlayers == null || localPlayers.Count == 0 || localPlayers.Contains(seat.PlayerID);
                ImGui.Spacing();
                UiText.Colored(ToVec4(colorFor(seat.PlayerID)), $"{seat.Name}'s cards:");
                DrawCards(state, system, side.Table, seat.SlotID, side, mine);
                if (mine && side.Hand.Count > 0)
                {
                    UiText.Disabled("  In hand:");
                    DrawCards(state, system, side.Hand, seat.SlotID, null, true);
                }
                else UiText.Disabled($"  {side.Hand.Count} in hand, {side.Deck.Count} in deck");
            }
        }
        else if (state.Game == EMissionCardGame.BattleOfWits)
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
            UiText.Disabled("Shared cards:");
            DrawCards(state, system, state.SharedTable, -1);
        }

        List<MissionScore> scored = state.Scored;
        if (scored.Count > 0)
        {
            ImGui.Spacing();
            UiText.Disabled("Scored:");
            foreach (MissionScore s in scored)
            {
                IPlayerSlotInfo? seat = seats.FirstOrDefault(p => p.SlotID == s.Slot);
                string who = seat?.Name ?? "?";
                MissionCardDef def = MissionCardCatalog.Card(system, s.Card);
                UiText.Wrapped($"  R{s.Round} {who}: {def.Title} +{s.Vp} VP");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(s.Text);
            }
        }
        UiText.Disabled("End of game: +1 VP per objective you control.");
    }

    private static void DrawCards(MissionCardState state, EGameSystem system, List<int> table, int prioritySlot,
        MissionCardSide? side = null, bool mine = true)
    {
        if (table.Count == 0)
        {
            UiText.Disabled("  (none)");
            return;
        }
        foreach (int number in table)
        {
            bool hidden = side != null && side.Hidden.Contains(number);
            string tags = (side != null && side.Challenge == number ? " [CHALLENGE +1]" : "")
                        + (side != null && side.Paragon == number ? " [PARAGON +1, Heroes only]" : "");
            if (hidden && !mine)
            {
                UiText.Wrapped($"  [??] Hidden card{tags}");
                continue;
            }
            MissionCardDef def = MissionCardCatalog.Card(system, number);
            int top = def.Conditions.Max(c => c.Vp);
            UiText.Wrapped($"  [{number}] {def.Title} - up to {top} VP{(hidden ? " (face down)" : "")}{tags}");
            if (!ImGui.IsItemHovered()) continue;

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(360f);
            UiText.Colored(Heading, $"{number} {def.Title}", wrap: false);
            foreach (MissionCondition c in def.Conditions)
                ImGui.TextUnformatted($"{c.Vp} VP: {c.Text}");
            if (number == 44 && state.Priority.TryGetValue(MissionCardState.PriorityKey(prioritySlot, 44), out int obj))
                UiText.Colored(Heading, $"Rolled: objective {obj}.");
            if (def.Note != null) UiText.Disabled(def.Note);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static Vector4 ToVec4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}
