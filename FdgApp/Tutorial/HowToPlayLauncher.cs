using FDG;
using FDG.ArmyBuilding;
using FDG.Rules;
using FDG.Ai;
using FDG.Players;
using FdgRaylib.Config;
using FdgRaylib.Lobby;

namespace FdgRaylib.Tutorial;

/// <summary>How far a player has got in How to Play, per game (saved in the config file).</summary>
public sealed class HowToPlayProgress
{
    /// <summary>Lessons finished, keyed by game (<see cref="EGameSystem"/> name).</summary>
    public Dictionary<string, int> LessonsDone { get; set; } = new();

    public int Done(EGameSystem system) => LessonsDone.TryGetValue(system.ToString(), out int n) ? n : 0;

    /// <summary>Records a finished lesson (never goes backwards) and saves the config.</summary>
    public static void Finished(EGameSystem system, int lesson)
    {
        var p = UserConfig.Current.HowToPlay;
        if (lesson <= p.Done(system)) return;
        p.LessonsDone[system.ToString()] = lesson;
        UserConfig.Save();
    }

    public static void Reset(EGameSystem system)
    {
        UserConfig.Current.HowToPlay.LessonsDone.Remove(system.ToString());
        UserConfig.Save();
    }
}

/// <summary>
/// Opens the practice battle: hosts a game on this computer, seats you and the enemy bot with the set armies, puts the
/// practice table down and sets everything to the core rules (no advanced rules, missions cards or house toggles that
/// the lessons don't teach), then launches. The dice are the practice battle's own (<see cref="ScriptedDiceRoller"/>,
/// handed to the engine through <see cref="FDG.GameModel.FDGServer.DiceRollerOverride"/>).
///
/// <para>The lobby's own remembered settings are not touched: the practice battle launches without saving them, so the
/// next ordinary game opens on whatever you played last.</para>
/// </summary>
public static class HowToPlayLauncher
{
    /// <summary>The dice of the practice battle in progress, or null.</summary>
    public static ScriptedDiceRoller? Dice { get; private set; }

    /// <summary>Which game the practice battle in progress teaches, or null.</summary>
    public static EGameSystem? Playing { get; private set; }

    /// <summary>Sets up and launches the practice battle. The session to hand to the battle screen, or null with why.</summary>
    public static LobbySession? Launch(EGameSystem system, out string error)
    {
        error = "";
        if (!HowToPlay.Teaches(system)) { error = "How to Play covers Grimdark Future and Age of Fantasy."; return null; }
        if (HowToPlay.BuildArmies(system, out string? why) is not { } armies) { error = why ?? "Couldn't build the practice armies."; return null; }
        if (!File.Exists(HowToPlay.MapPath)) { error = "The practice table file is missing."; return null; }
        if (!LobbySession.TryHost(system, EGameMode.Standard, out LobbySession? session, out error) || session == null) return null;

        var vm = session.Vm;
        // The core rules, and nothing the lessons don't teach.
        vm.SetRandomnessType(ERandomnessType.Realistic);
        vm.SetNumberOfRounds(HowToPlay.Rounds);
        vm.SetForceOrg(false);
        vm.SetExtraActions(EExtraActions.None);
        vm.SetAdvancedRules(EAdvancedRules.None);
        vm.SetChallengeBonuses(EChallengeBonuses.None);
        vm.SetDeploymentStyle(EDeploymentStyle.Core);
        vm.SetMissionCardGame(FDG.Missions.EMissionCardGame.Off);
        vm.SetExtraMission(FDG.Missions.EExtraMission.None);
        vm.SetSideMissions(FDG.Missions.ESideMissionPlay.Off);
        vm.SetKingOfTheHill(false);
        vm.SetSolidBuildings(false);
        vm.SetFastPlay(false, false, false);
        vm.SetFastPlaySimplifiedTerrain(false);
        vm.SetUnlimitedSplitFire(false);
        // The book: "Units may only be within 1" of other units when charging" - lesson 5 teaches it as written.
        vm.SetFriendlyUnitsMayOverlap(false);
        vm.SetPickDangerousTerrain(false);
        vm.SetTerrainPlacementMode(ETerrainPlacementMode.LoadFromFile);
        vm.SetTerrainLayoutPath(HowToPlay.MapPath);
        vm.SetObjectivePlacementMode(EObjectivePlacementMode.AutoPlaced);

        // You, and one enemy bot.
        PlayerID host = vm.PlayerInfos[0].PlayerID;
        foreach (var extra in vm.PlayerInfos.Skip(1).ToList())
            if (vm.CheckCanRemovePlayer(extra.PlayerID)) vm.RemovePlayer(extra.PlayerID);
        vm.AddAiPlayer(EAiProfile.SoloCoOp);
        if (vm.PlayerInfos.LastOrDefault(p => p.PlayerID.ID != host.ID && p.PlayerType == EPlayerType.AI) is not { } bot)
        {
            session.Dispose();
            error = "Couldn't seat the enemy.";
            return null;
        }
        vm.SetArmyPoints(Math.Max(armies.Mine.TotalPoints, armies.Foe.TotalPoints));
        session.GiveArmy(host, armies.Mine);
        session.GiveArmy(bot.PlayerID, armies.Foe);

        var dice = new ScriptedDiceRoller(HowToPlay.DiceSeed);
        dice.Plan(HowToPlay.RollOffs);
        Dice = dice;
        Playing = system;
        // One-shot: the first game built takes the practice dice and clears the hook, so no later game can get them.
        FDG.GameModel.FDGServer.DiceRollerOverride = _ =>
        {
            FDG.GameModel.FDGServer.DiceRollerOverride = null;
            return dice;
        };
        if (!session.Launch(remember: false))
        {
            error = session.LastLaunchError ?? "Launch failed.";
            Stop();
            session.Dispose();
            return null;
        }
        return session;
    }

    /// <summary>The practice battle is over (or left).</summary>
    public static void Stop()
    {
        Dice = null;
        Playing = null;
        FDG.GameModel.FDGServer.DiceRollerOverride = null;
    }
}
