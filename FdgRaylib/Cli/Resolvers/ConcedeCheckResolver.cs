using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #466 - the text interface has no Concede button and the book makes conceding optional, so a CLI
/// player plays every campaign mission out (no prompt each round, which would stall piped play).
/// </summary>
public class ConcedeCheckResolver : IStageResolver<ConcedeCheckRequest, bool>
{
    public Task<bool> Resolve(ConcedeCheckRequest request) => Task.FromResult(false);
}
