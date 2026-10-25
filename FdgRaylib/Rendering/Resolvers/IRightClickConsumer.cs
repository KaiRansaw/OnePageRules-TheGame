namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Marks a table tool that uses right-click itself - the app-wide "undo the last step" gesture (#343) in
/// movement, placement and consolidation, and back / cancel in objective and terrain placement. While one
/// of these is active, a plain right-click on a unit is left to it, and Shift + right-click opens the
/// unit's info card instead (see <see cref="GuiResolverOverlay.ActiveToolUsesRightClick"/>).
/// </summary>
public interface IRightClickConsumer
{
}
