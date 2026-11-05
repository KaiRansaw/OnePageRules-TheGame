using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #456 - Fast-Play unit leaders on stdin. Lists every unit with its suggested leader and takes changes as
/// "unit=model" pairs ("2=3 5=1"). A blank line, or EOF, keeps the suggestions - leaders are compulsory, so
/// "keep what the game picked" is the safe default for piped play.
/// </summary>
public class PickUnitLeadersResolver
    : IStageResolver<PickUnitLeadersRequest, CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>
{
    public Task<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>> Resolve(PickUnitLeadersRequest request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);
        for (int u = 0; u < request.Units.Count; u++)
        {
            PickUnitLeadersRequest.UnitEntry unit = request.Units[u];
            Console.WriteLine($"  [{u + 1}] {unit.UnitName}");
            for (int m = 0; m < unit.Models.Count; m++)
                Console.WriteLine($"        ({m + 1}) {unit.Models[m].Description}"
                                  + (unit.Models[m].Model.Equals(unit.Suggested) ? "  <- suggested" : ""));
        }
        Console.WriteLine("  (changes as unit=model, e.g. \"2=3\"; blank keeps the suggestions)");

        List<PickUnitLeadersRequest.LeaderPick> picks = request.Suggested();
        while (true)
        {
            Console.Write("Leaders: ");
            string? input = Console.ReadLine();
            if (input == null || input.Trim().Length == 0) return Done(picks);

            var changed = new List<PickUnitLeadersRequest.LeaderPick>(picks);
            bool ok = true;
            foreach (string part in input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=');
                if (pair.Length != 2 || !int.TryParse(pair[0], out int u) || !int.TryParse(pair[1], out int m)
                    || u < 1 || u > request.Units.Count || m < 1 || m > request.Units[u - 1].Models.Count)
                {
                    Console.WriteLine("Use unit=model with the numbers shown, e.g. 2=3.");
                    ok = false;
                    break;
                }
                PickUnitLeadersRequest.UnitEntry unit = request.Units[u - 1];
                changed[u - 1] = new PickUnitLeadersRequest.LeaderPick(unit.Unit, unit.Models[m - 1].Model);
            }
            if (ok) return Done(changed);
        }
    }

    private static Task<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>> Done(
        List<PickUnitLeadersRequest.LeaderPick> picks) =>
        Task.FromResult<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>(
            new Selected<List<PickUnitLeadersRequest.LeaderPick>>(picks));
}
