using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Game;

/// <summary>
/// "Hold left click and the unit turns towards the mouse" (Kai, 2026-11-12), for deployment and moves: the facing to give
/// a placed or moved unit, and the facing each preview model shows (its small arrow).
///
/// <para>Only round bases take a chosen facing: a round base's facing changes nothing the rules measure, so turning it is
/// free. An oval or rectangle base's facing is the ground it covers - it keeps the facing its placement or move was
/// checked with (FacingRules in the engine).</para>
/// </summary>
public static class FacingAim
{
    /// <summary>The unit direction from the unit's middle to the pointer, or null when the pointer is on the middle.</summary>
    public static Float2? From(float centreX, float centreZ, float pointerX, float pointerZ, float deadZone = 0.75f)
    {
        float dx = pointerX - centreX, dz = pointerZ - centreZ;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        return len < deadZone ? null : new Float2(dx / len, dz / len);
    }

    /// <summary>The placement with every round-base model turned to <paramref name="facing"/>.</summary>
    public static List<PlacedObjectEntry<ModelData>> Turn(List<PlacedObjectEntry<ModelData>> plan, Float2 facing) =>
        plan.Select(e => FacingRules.TurnsFreely(e.Binding.GetValue()) ? e with { Facing = facing } : e).ToList();

    /// <summary>
    /// The move with every round-base model ending turned to <paramref name="facing"/>: each earlier waypoint faces the
    /// way the model walks it (as a move with no facings would), the last one the chosen way.
    /// </summary>
    public static List<ModelMoveEntry> Turn(List<ModelMoveEntry> moves, Float2 facing) => moves.Select(e =>
    {
        ModelData model = e.Model.GetValue();
        if (!FacingRules.TurnsFreely(model) || e.Positions.Count == 0) return e;
        var facings = new List<Float2>(e.Positions.Count);
        Position from = model.Position;
        for (int i = 0; i < e.Positions.Count; i++)
        {
            facings.Add(i == e.Positions.Count - 1 ? facing : FacingRules.Toward(from, e.Positions[i]) ?? model.Facing);
            from = e.Positions[i];
        }
        return e with { Facings = facings };
    }).ToList();

    /// <summary>Which way a placed model will look: the placement's facing, else the model's own.</summary>
    public static Float2 Shown(PlacedObjectEntry<ModelData> e) => e.Facing ?? e.Binding.GetValue().Facing;

    /// <summary>Which way a moved model will look when it arrives: its last facing given, else the way it walked the last leg.</summary>
    public static Float2 Shown(ModelMoveEntry e)
    {
        ModelData model = e.Model.GetValue();
        if (e.Facings is { Count: > 0 } f) return f[^1];
        if (!FacingRules.TurnsFreely(model) || e.Positions.Count == 0) return model.Facing;
        Position from = e.Positions.Count > 1 ? e.Positions[^2] : model.Position;
        return FacingRules.Toward(from, e.Positions[^1]) ?? model.Facing;
    }
}
