using Godot;
using OprGodot.Mock;

namespace OprGodot.Hud;

/// <summary>
/// The 3D table behind every menu screen: the stand-in board from the HUD mockups (no labels), with the camera
/// circling it slowly. Swapped for a real saved map once the table is ported (step 3).
/// </summary>
public partial class MenuBackdrop : Node
{
    private MockTable _table = null!;
    private float _angle = 2.3f;
    public bool Orbit { get; set; } = true;

    /// <summary>The one backdrop (Main adds it). A game in progress hides it and puts it back on the way out.</summary>
    public static MenuBackdrop? Instance { get; private set; }

    public void SetActive(bool on)
    {
        // Taken out of the tree, not hidden: its environment and camera would fight the battle table's.
        if (on && _table.GetParent() == null) { AddChild(_table); _table.Camera.MakeCurrent(); Place(); }
        else if (!on && _table.GetParent() == this) RemoveChild(_table);
        SetProcess(on);
    }

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready()
    {
        _table = new MockTable { ShowLabels = false };
        AddChild(_table);
        CallDeferred(nameof(Place));
    }

    private void Place()
    {
        float r = 62;
        _table.Look(new Vector3(Mathf.Cos(_angle) * r, 16, Mathf.Sin(_angle) * r), new Vector3(0, 0, -2), 38);
    }

    public override void _Process(double delta)
    {
        if (!Orbit) return;
        _angle += (float)delta * 0.025f;
        Place();
    }
}
