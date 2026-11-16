using System.Text.Json;
using FDG;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using FDG.Rules.Serialization;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Developer aid (explicit only): plays a bot game with the battle report listening and writes its blocks out.</summary>
[TestFixture, Explicit]
public class ReportCaptureDev
{
    private sealed class Watched : ComputerPlayerController, IPlayerController
    {
        private readonly IPresentationSink _sink;
        public Watched(string n, PlayerID id, FDGGame_AsLocal g, FDG.StageResolution.IStageResolverRegistry r, IPresentationSink sink) : base(n, id, g, r) => _sink = sink;
        IPresentationSink? IPlayerController.PresentationSink => _sink;
    }

    [Test]
    public async Task Capture()
    {
        string root = TestContext.CurrentContext.TestDirectory;
        while (!Directory.Exists(Path.Combine(root, "FdgLab"))) root = Path.GetDirectoryName(root)!;
        ArmyListFile Load(string f) => JsonSerializer.Deserialize<ArmyListFile>(File.ReadAllText(Path.Combine(root, "FdgLab", "armies", f)), RuleJson.Options)!;
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bus = new DevBus();
        var armies = new[] { Load("Orks 2k - Horde Mixed.fdgarmy"), Load("Alien Hives 2k - Horde Melee.fdgarmy") };
        var slots = new PlayerSlot[2];
        BattleReport? report = null;
        for (int i = 0; i < 2; i++)
        {
            slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), armies[i], store);
            var g = new FDGGame_AsLocal(store, bus);
            var reg = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, g.TableState, slots[i].PlayerID, 77, i);
            if (i == 0) { report = new BattleReport(g.TableState); slots[i].AssignPlayerController(new Watched("AI 0", slots[i].PlayerID, g, reg, report)); }
            else slots[i].AssignPlayerController(new ComputerPlayerController("AI 1", slots[i].PlayerID, g, reg));
        }
        var settings = GameSettings.GetDefault();
        settings.RandomnessType = ERandomnessType.Realistic;
        settings.DiceSeed = 77;
        var done = new TaskCompletionSource<GameResult>();
        var server = new FDGServer(store, bus, settings, slots);
        server.OnGameCompleted += r => done.TrySetResult(r);
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(200)));
        var sb = new System.Text.StringBuilder();
        foreach (var b in BattleReport.Blocks(report!.Snapshot()))
        {
            sb.AppendLine($"=== BLOCK round {b.Round} unit {b.Unit} moved {b.Moved:0.0} shot {b.Shot} fought {b.Fought} charged {b.Charged}");
            if (b.Activation != null) sb.AppendLine("  ACT " + string.Concat(b.Activation.Title.Select(p => p.Text)));
            foreach (var l in b.Lines)
            {
                sb.AppendLine($"  [{l.Kind}] {string.Concat(l.Title.Select(p => p.Kind == BattleReport.PieceKind.Plain ? p.Text : "<" + p.Kind + ":" + p.Text + ">"))} | body: {l.Body}");
                if (l.Attack is { } a) sb.AppendLine($"     outcome melee={a.Melee} hits={a.Hits} att={a.Attacks} wounds={a.Wounds} lost={a.ModelsLost} routed={a.Routed}");
                foreach (var r in l.Rolls) sb.AppendLine($"     roll {r.Who}: {r.Label} ({r.Threshold}+) {string.Join(" ", r.Faces)} -> {r.Result} [{string.Join(", ", r.Modifiers)}]");
                foreach (var d in l.Details) sb.AppendLine($"     detail {d}");
            }
            if (ReportCard.For(b) is { } c)
            {
                sb.AppendLine($"  CARD: {c.Headline.ToUpperInvariant()} | {c.Subline}");
                foreach (var a in c.Attacks)
                    sb.AppendLine($"    {a.ByName} {a.Verb} {a.TargetName}: " + string.Join(" -> ", a.Steps.Select(s => $"{s.Value} {s.Label}")) + (a.Notes.Count > 0 ? "  notes: " + string.Join("; ", a.Notes) : ""));
                foreach (var r in c.After) sb.AppendLine($"    AFTER {r.Text} [{r.Tag}]");
            }
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("FDG_CAPTURE") ?? Path.Combine(Path.GetTempPath(), "report-capture.txt"), sb.ToString());
    }
}
