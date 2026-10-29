using FDG;
using FdgRaylib.Rendering.Presentation;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Performs mouse-vs-table hit testing once per frame and caches the results so that
/// multiple systems (tooltip overlay, resolver canvas overlays) share the same answer
/// without redoing the math.
///
/// Call Update() at the start of the ImGui frame each game tick.
/// </summary>
public class TableHitTester
{
    public IUnit?    HoveredUnit    { get; private set; }
    public IModel?   HoveredModel   { get; private set; }
    public ITerrain? HoveredTerrain { get; private set; }

    /// <summary>The objective marker under the cursor, when no model is (models win, then objectives,
    /// then terrain).</summary>
    public IObjective? HoveredObjective { get; private set; }

    /// <summary>How close to an objective's centre the cursor must be to hover it - generous next to the
    /// 0.5" marker so it is easy to land on, but well inside the 3" control ring so terrain under the
    /// rest of the ring still answers.</summary>
    public const float ObjectiveHoverRadiusInches = 1.5f;

    /// <summary>Left mouse button clicked this frame, not consumed by an ImGui widget.</summary>
    public bool Clicked { get; private set; }

    /// <summary>
    /// Whether a model can be pointed at. Alive models always can. A downed Star Quest hero is
    /// <see cref="IModel.GetIsDead"/> but is still DRAWN on the table (greyed, by
    /// <c>PresentationPlayer.GetModelDrawState</c>), and the party has to be able to walk over and
    /// revive it - so it must be clickable too.
    /// <para>
    /// Before this, the hit tester skipped every model that was not alive, so an unconscious hero
    /// could be seen but not selected: no hover card, nothing in Active Unit or Actions, and the
    /// Revive skill had no way to target it (QA 9.20/9.21/3.24). The test mirrors the renderer's
    /// own predicate exactly, so anything drawn can be picked and nothing invisible can.
    /// </para>
    /// </summary>
    private static bool CanBePointedAt(IModel model) =>
        model.GetIsAlive()
        || (model.GetIsDead() && PresentationPlayer.IsUnconsciousHero?.Invoke(model) == true);

    public void Update(ITableState tableState, float scale, int originX, int originY, float tableH)
    {
        HoveredUnit    = null;
        HoveredModel   = null;
        HoveredTerrain = null;
        HoveredObjective = null;

        var io        = ImGui.GetIO();
        bool owned    = io.WantCaptureMouse;
        Clicked       = !owned && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (owned) return;

        var   mousePos   = TableInput.MousePos;
        float tableX     = (mousePos.X - originX) / scale;
        float tableZ     = tableH - (mousePos.Y - originY) / scale;

        // Hit-test models — closest centre wins
        float bestDist = float.MaxValue;
        foreach (var unit in tableState.Units.Objects)
        {
            // Off-table units (Ambush reserve, embarked, flown-off Aircraft) park at the origin.
            if (!unit.GetIsOnBattlefield()) continue;

            foreach (var model in unit.Models)
            {
                if (!CanBePointedAt(model)) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;

                float dx   = tableX - pos.x;
                float dz   = tableZ - pos.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                // Hit-test the true base shape (#149); dist still breaks ties between overlapping bases.
                if (model.BaseShape.ContainsLocalPoint(dx, dz) && dist < bestDist)
                {
                    bestDist     = dist;
                    HoveredModel = model;
                    HoveredUnit  = unit;
                }
            }
        }

        // 3D view: a model's upright card stands above its base, so pointing at the card picks that model
        // (it wins over whatever bit of table happens to sit behind it).
        if (TableInput.CardHit is ModelID cardHit)
        {
            foreach (var unit in tableState.Units.Objects)
            {
                if (!unit.GetIsOnBattlefield()) continue;
                foreach (var model in unit.Models)
                {
                    if (!CanBePointedAt(model) || !model.ID.Equals(cardHit)) continue;
                    HoveredModel = model;
                    HoveredUnit  = unit;
                }
            }
        }

        // Objectives next - closest marker within the hover radius.
        if (HoveredUnit == null)
        {
            float bestObjective = ObjectiveHoverRadiusInches;
            foreach (var objective in tableState.Objectives.Objects)
            {
                float dx = tableX - objective.Position.x;
                float dz = tableZ - objective.Position.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist <= bestObjective)
                {
                    bestObjective    = dist;
                    HoveredObjective = objective;
                }
            }
        }

        // Hit-test terrain only when no model or objective is under the cursor
        if (HoveredUnit == null && HoveredObjective == null)
        {
            foreach (var terrain in tableState.Terrain.Objects)
            {
                if (terrain.IsPointWithinZone(new Float2(tableX, tableZ)))
                {
                    HoveredTerrain = terrain;
                    break;
                }
            }
        }
    }
}
