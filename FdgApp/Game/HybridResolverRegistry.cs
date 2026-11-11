using FDG.Data;
using FDG.StageResolution;

namespace FdgRaylib.Game;

/// <summary>
/// The answer to "who decides this?" while a front end is still being built: every request type the front end
/// has a resolver for goes to the player; every other type is answered by the official bot on the player's
/// behalf ("autopilot"), so a whole game can be played - and watched - before every decision has a screen.
/// <see cref="LastAutopilotTask"/> says what was last decided that way, so the screen can show it.
///
/// <para>Godot port step 4 fills the front-end side in one request type at a time; when it is complete the
/// autopilot side is never reached. One local human per machine for now (the autopilot plays as
/// <c>LocalPlayerIDs[0]</c>); hot-seat games need a per-player autopilot - noted in the port plan.</para>
/// </summary>
public sealed class HybridResolverRegistry : IStageResolverRegistry
{
    private readonly StageResolverRegistry _front = new();
    private readonly IStageResolverRegistry _autopilot;
    private readonly HashSet<Type> _frontTypes = new();
    private readonly HashSet<string> _frontNames = new();

    public HybridResolverRegistry(IStageResolverRegistry autopilot) => _autopilot = autopilot;

    /// <summary>The last request the bot answered for the player, in plain words (null until one is).</summary>
    public string? LastAutopilotTask { get; private set; }

    public bool HasFrontEnd(Type requestType) => _frontTypes.Contains(requestType);

    public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
        where TRequest : IStageTaskRequest<TReply>
    {
        _front.RegisterResolver(resolver);
        _frontTypes.Add(typeof(TRequest));
        if (typeof(TRequest).FullName is { } name) _frontNames.Add(name);
        return this;
    }

    public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
    {
        if (_frontTypes.Contains(typeof(TRequest))) return _front.ResolveRequest<TRequest, TReply>(request);
        LastAutopilotTask = request.DisplayName;
        return _autopilot.ResolveRequest<TRequest, TReply>(request);
    }

    public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore gameDataStore)
    {
        if (_frontNames.Contains(typeFullName)) return _front.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
        LastAutopilotTask = Friendly(typeFullName);
        return _autopilot.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
    }

    /// <summary>"FDG.StateMachine.ChooseRangedAttackRequest" -> "choose ranged attack".</summary>
    private static string Friendly(string fullName)
    {
        string name = fullName;
        int generic = name.IndexOf('`');
        if (generic >= 0) name = name[..generic];
        name = name[(name.LastIndexOf('.') + 1)..];
        if (name.EndsWith("Request")) name = name[..^"Request".Length];
        return string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
