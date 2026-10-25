namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A table tool with its own Ctrl+Z / Ctrl+X (deployment's unit undo / redo, movement planning's waypoint
/// undo / redo). While one is active and using them, the game-wide Take Back keys stand aside - the
/// Take Back / Redo buttons in the top bar still work (see <see cref="GuiResolverOverlay.ActiveToolUsesUndoKeys"/>).
/// </summary>
public interface IUndoKeyConsumer
{
    /// <summary>True while this tool's current request uses Ctrl+Z / Ctrl+X itself.</summary>
    bool UsesUndoKeys { get; }
}
