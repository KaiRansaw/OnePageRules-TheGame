using FDG;
using FDG.Data;
using FDG.StageResolution;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Godot hot-seat: with two local players on one computer, a decision the screen doesn't take yet is answered by
/// the bot for the player it is addressed to - not always player 1's bot.
/// </summary>
[TestFixture]
public class HybridResolverHotSeatTests
{
    private sealed class Recorder : IStageResolverRegistry
    {
        public readonly string Name;
        public readonly List<string> Seen = new();
        public Recorder(string name) => Name = name;
        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply> => this;
        public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
        { Seen.Add(request.TaskName); return Task.FromResult(default(TReply)!); }
        public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore gameDataStore)
        { Seen.Add(requestJson); return Task.FromResult("0"); }
    }

    [Test]
    public void EachLocalPlayersDecision_GoesToThatPlayersBot()
    {
        var p1 = new PlayerID(Guid.NewGuid());
        var p2 = new PlayerID(Guid.NewGuid());
        var bot1 = new Recorder("p1");
        var bot2 = new Recorder("p2");
        var hybrid = new HybridResolverRegistry(bot1, new Dictionary<PlayerID, IStageResolverRegistry> { [p1] = bot1, [p2] = bot2 });

        Assert.That(hybrid.AutopilotFor(p1), Is.SameAs(bot1));
        Assert.That(hybrid.AutopilotFor(p2), Is.SameAs(bot2));
        Assert.That(hybrid.AutopilotFor(new PlayerID(Guid.NewGuid())), Is.SameAs(bot1), "an unknown player falls back to the first bot");

        string json = "{\"TargetPlayerID\":{\"ID\":\"" + p2.ID + "\"},\"Other\":{\"ID\":\"" + p1.ID + "\"}}";
        hybrid.ResolveRequestAsJson("FDG.Some.ThingRequest", json, null!);
        Assert.That(bot2.Seen, Has.Count.EqualTo(1), "a networked request is routed by its TargetPlayerID");
        Assert.That(bot1.Seen, Is.Empty);
    }

    [Test]
    public void OnePlayerComputer_KeepsTheSingleBot()
    {
        var bot = new Recorder("only");
        var hybrid = new HybridResolverRegistry(bot);
        hybrid.ResolveRequestAsJson("FDG.Some.ThingRequest", "{\"TargetPlayerID\":{\"ID\":\"" + Guid.NewGuid() + "\"}}", null!);
        Assert.That(bot.Seen, Has.Count.EqualTo(1));
    }
}
