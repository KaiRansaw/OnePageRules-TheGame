using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;

namespace OprGodot.Lab;

/// <summary>
/// Puts a loaded model on its base from its saved settings (<see cref="UnitModelSettings"/>): stand it up, turn it,
/// size it, feet on the base. One place for it, so the Asset Lab, the facing review and (later) the battle table all
/// stand a model the same way.
/// </summary>
public static class ModelPlacement
{
    /// <summary>
    /// Sets <paramref name="pivot"/>'s transform so <paramref name="body"/> (a child of it) stands as the settings say.
    /// The pivot's parent is the spot on the table; all three must be in the scene tree (bounds are measured there).
    /// Returns the pivot's transform and the finished model's box, in the parent's space.
    /// </summary>
    public static (Transform3D Transform, Aabb Box) Place(Node3D pivot, Node3D body, UnitModelSettings s)
    {
        Basis up = s.UpAxis switch
        {
            EUpAxis.ZUp => new Basis(Vector3.Right, -Mathf.Pi / 2),
            EUpAxis.ZDown => new Basis(Vector3.Right, Mathf.Pi / 2),
            EUpAxis.YDown => new Basis(Vector3.Right, Mathf.Pi),
            EUpAxis.XUp => new Basis(Vector3.Back, Mathf.Pi / 2),
            EUpAxis.XDown => new Basis(Vector3.Back, -Mathf.Pi / 2),
            _ => Basis.Identity,
        };
        Basis turn = new Basis(Vector3.Up, Mathf.DegToRad(s.YawDegrees)) * up;
        pivot.Transform = new Transform3D(turn, Vector3.Zero);
        var holder = (Node3D)pivot.GetParent();
        Aabb raw = AssetLoader.Bounds(body, holder) ?? new Aabb(Vector3.Zero, Vector3.One);
        float h = Mathf.Max(raw.Size.Y, 1e-4f);
        float k = s.HeightMm > 0 ? s.HeightMm / UnitModelSettings.MmPerInch / h : 1f;
        Vector3 centre = raw.GetCenter() * k;
        float footY = (s.BaseMm > 0 ? TableLook.BaseTop : 0f) - raw.Position.Y * k;
        Vector3 offset = s.CentreOnBase ? new Vector3(-centre.X, footY, -centre.Z) : new Vector3(0, footY, 0);
        pivot.Transform = new Transform3D(turn.Scaled(Vector3.One * k), offset);
        return (pivot.Transform, new Aabb(raw.Position * k + offset, raw.Size * k));
    }
}
