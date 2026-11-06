using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #466 - answers the round-start concession check at once, with no prompt: yes if the player pressed
/// Concede on the Mission tab. Kai chose a button over a question every round (2026-11-06).
/// </summary>
public sealed class GuiConcedeCheckResolver : IStageResolver<ConcedeCheckRequest, bool>
{
    private readonly ConcedeIntent _intent;

    public GuiConcedeCheckResolver(ConcedeIntent intent) => _intent = intent;

    public Task<bool> Resolve(ConcedeCheckRequest request) => Task.FromResult(_intent.Has(request.TargetPlayerID));
}
