using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #439 - "pick up to N friendly units for this Banner/Musician" on stdin. One prompt for all the picks
/// (Kai, 2026-11-01), taking a space- or comma-separated list of numbers.
///
/// <para>
/// Declining is a legal answer, because the rule is "UP TO 2": a blank line picks nothing. <b>EOF picks
/// nothing too</b>, which is the right default for piped play - the alternative, auto-picking the first
/// options, would mean every headless game silently granted buffs nobody chose, and a headless run is
/// how the smoke test and the sweeps play.
/// </para>
/// </summary>
public class PickCommandGroupUnitsResolver
    : IStageResolver<PickCommandGroupUnitsRequest, CancellableResult<List<DataBinding<UnitData>>>>
{
    public Task<CancellableResult<List<DataBinding<UnitData>>>> Resolve(
        PickCommandGroupUnitsRequest request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);

        for (int i = 0; i < request.ValidOptions.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {request.ValidOptions[i].Name}");
        }

        Console.WriteLine($"  (enter up to {request.MaxPicks} numbers, or blank for none)");

        while (true)
        {
            Console.Write("Picks: ");
            string? input = Console.ReadLine();

            if (input == null) return Done(new List<DataBinding<UnitData>>());   // EOF: pick nothing

            input = input.Trim();
            if (input.Length == 0) return Done(new List<DataBinding<UnitData>>());

            string[] parts = input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > request.MaxPicks)
            {
                Console.WriteLine($"At most {request.MaxPicks}.");
                continue;
            }

            var picks = new List<DataBinding<UnitData>>();
            bool ok = true;
            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int choice)
                    || choice < 1 || choice > request.ValidOptions.Count)
                {
                    Console.WriteLine($"Enter numbers between 1 and {request.ValidOptions.Count}.");
                    ok = false;
                    break;
                }

                DataBinding<UnitData> picked = request.ValidOptions[choice - 1].Option;
                if (picks.Contains(picked))
                {
                    Console.WriteLine("That unit is already picked.");
                    ok = false;
                    break;
                }

                picks.Add(picked);
            }

            if (ok) return Done(picks);
        }
    }

    private static Task<CancellableResult<List<DataBinding<UnitData>>>> Done(
        List<DataBinding<UnitData>> picks)
    {
        CancellableResult<List<DataBinding<UnitData>>> result =
            new Selected<List<DataBinding<UnitData>>>(picks);
        return Task.FromResult(result);
    }
}
