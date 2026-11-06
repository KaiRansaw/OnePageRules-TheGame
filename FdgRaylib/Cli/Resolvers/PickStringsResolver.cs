using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #462 - pick several from a list on stdin: space- or comma-separated numbers, blank for the suggestion
/// when it is legal, EOF always takes the suggestion (so piped play never stalls).
/// </summary>
public class PickStringsResolver : IStageResolver<PickStringsRequest, List<string>>
{
    public Task<List<string>> Resolve(PickStringsRequest request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);
        for (int i = 0; i < request.Options.Count; i++)
        {
            string option = request.Options[i];
            string extra = request.Descriptions != null && request.Descriptions.TryGetValue(option, out string? d) ? $" - {d}" : "";
            Console.WriteLine($"  [{i + 1}] {option}{extra}");
        }
        string bounds = request.MinPicks == request.MaxPicks ? $"exactly {request.MaxPicks}" : $"{request.MinPicks} to {request.MaxPicks}";
        Console.WriteLine($"  (enter {bounds} numbers; blank for the suggestion: {string.Join(", ", request.Suggested)})");

        while (true)
        {
            Console.Write("Picks: ");
            string? input = Console.ReadLine();
            if (input == null || input.Trim().Length == 0) return Task.FromResult(request.Suggested.ToList());

            var picks = new List<string>();
            bool ok = true;
            foreach (string part in input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int n) || n < 1 || n > request.Options.Count)
                {
                    ok = false;
                    break;
                }
                picks.Add(request.Options[n - 1]);
            }
            if (ok && request.Legal(picks)) return Task.FromResult(picks);
            Console.WriteLine($"Enter {bounds} different numbers between 1 and {request.Options.Count}.");
        }
    }
}
