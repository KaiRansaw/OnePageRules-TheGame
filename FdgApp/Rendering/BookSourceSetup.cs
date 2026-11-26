using System.Runtime.CompilerServices;
using FDG.ArmyBuilding;

namespace FdgRaylib.Rendering;

/// <summary>
/// Hands the engine the bundled army books (<see cref="BookSource.Find"/>), so a bot can buy units in the middle of a
/// game (Meat Grinder recruitment). Runs once when this assembly loads - both front ends and the headless runner load
/// it before any game starts. The books are only parsed the first time a bot actually shops.
/// </summary>
internal static class BookSourceSetup
{
    [ModuleInitializer]
    internal static void Install()
    {
        BookSource.Find ??= (faction, armyName, slug) =>
        {
            List<BookFile> books = BookLibrary.Load();
            var matches = books.Where(b => b.MatchesFactionName(faction) || b.MatchesFactionName(armyName)).ToList();
            return matches.FirstOrDefault(b => slug != null && GameSystems.SameSystem(b.GameSystem, slug)) ?? matches.FirstOrDefault();
        };
    }
}
