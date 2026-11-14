using FDG;
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
/// autopilot side is never reached. Hot-seat (two or more local players on one computer): each local player has
/// their own autopilot, and a request goes to the one for the player it is addressed to
/// (<see cref="IStageTaskRequest.TargetPlayerID"/>) - a bot built for player 1 would otherwise play player 2's
/// decisions as if they were player 1's (wrong side's units, wrong "friendly").</para>
/// </summary>
public sealed class HybridResolverRegistry : IStageResolverRegistry
{
    private readonly StageResolverRegistry _front = new();
    private readonly IStageResolverRegistry _autopilot;
    private readonly IReadOnlyDictionary<PlayerID, IStageResolverRegistry> _perPlayer;
    private readonly HashSet<Type> _frontTypes = new();
    private readonly HashSet<string> _frontNames = new();

    /// <param name="autopilot">The bot for the first local player - and for any request addressed to nobody it knows.</param>
    /// <param name="perPlayer">Hot-seat: every local player's own bot. Empty or null for a one-player computer.</param>
    public HybridResolverRegistry(IStageResolverRegistry autopilot, IReadOnlyDictionary<PlayerID, IStageResolverRegistry>? perPlayer = null)
    {
        _autopilot = autopilot;
        _perPlayer = perPlayer ?? new Dictionary<PlayerID, IStageResolverRegistry>();
    }

    /// <summary>The bot that answers for <paramref name="player"/>.</summary>
    public IStageResolverRegistry AutopilotFor(PlayerID? player) =>
        player is { } p && _perPlayer.TryGetValue(p, out IStageResolverRegistry? bot) ? bot : _autopilot;

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
        return AutopilotFor(request.TargetPlayerID).ResolveRequest<TRequest, TReply>(request);
    }

    public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore gameDataStore)
    {
        if (_frontNames.Contains(typeFullName)) return _front.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
        LastAutopilotTask = Friendly(typeFullName);
        return AutopilotFor(TargetOf(requestJson)).ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
    }

    /// <summary>Which of the local players a request sent as JSON is addressed to (its TargetPlayerID), or null.</summary>
    private PlayerID? TargetOf(string requestJson)
    {
        if (_perPlayer.Count < 2) return null;
        try
        {
            string? guid = Newtonsoft.Json.Linq.JObject.Parse(requestJson)["TargetPlayerID"]?["ID"]?.ToString();
            return Guid.TryParse(guid, out Guid g) ? new PlayerID(g) : null;
        }
        catch (Newtonsoft.Json.JsonException) { return null; }
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
