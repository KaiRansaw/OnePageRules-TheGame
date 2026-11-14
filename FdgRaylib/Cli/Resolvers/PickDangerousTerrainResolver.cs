using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// The Generic terrain generator's "pick D3 pieces each to count as Dangerous" on stdin. Mandatory. EOF resolves
/// to the first piece so piped play works.
/// </summary>
public class PickDangerousTerrainResolver : IStageResolver<PickDangerousTerrainRequest, int>
{
    public Task<int> Resolve(PickDangerousTerrainRequest request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);
        for (int i = 0; i < request.Options.Count; i++)
        {
            PickDangerousTerrainRequest.TerrainOption o = request.Options[i];
            string name = string.IsNullOrWhiteSpace(o.Name) ? "Terrain" : o.Name;
            Console.WriteLine($"  [{i + 1}] {name} at ({o.X:0}\", {o.Z:0}\") - {FDG.TerrainBook.Describe(o.Rules)}");
        }

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            if (input == null) return Task.FromResult(0);
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= request.Options.Count)
                return Task.FromResult(choice - 1);
            Console.WriteLine($"Enter a number between 1 and {request.Options.Count}.");
        }
    }
}
