using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

// #103 — stdin resolver for the cast-assist prompt: how much this Caster spends to boost (+1 each,
// friendly) or disrupt (-1 each, enemy) another Caster's cast. EOF / blank input defaults to 0 (spend
// nothing), so piped/headless games never spend assist tokens unprompted.
//
// #414 — two prompts now, not one: spell tokens, then (Star Quest heroes, GFSQ v2.1.0) Power. Both are
// +1/-1 per point, but a hero's Power is also its revive and extra-action budget, so which one pays for a
// counter-spell has to be the player's choice. A caster holding only Power is asked only about Power.
public class CastAssistResolver : IStageResolver<CastAssistRequest, CastAssistReply>
{
    public Task<CastAssistReply> Resolve(CastAssistRequest request)
    {
        string assister = request.AssistingUnit.GetValue().Name;
        string caster   = request.CastingUnit.GetValue().Name;
        string verb     = request.IsFriendly ? "help" : "disrupt";
        string sign     = request.IsFriendly ? "+1" : "-1";

        Console.WriteLine();
        string holdings = $"{request.AvailableTokens} spell token{(request.AvailableTokens == 1 ? "" : "s")}";
        if (request.AvailablePower > 0) holdings += $", {request.AvailablePower} power";
        Console.WriteLine($"{assister} ({holdings}): spend how many to {verb} {caster}'s cast of " +
            $"{request.SpellName}? ({sign} each)");
        // What the spell does, indented like the spell picker's subtext - the name alone says nothing
        // about whether the cast is worth swaying.
        if (!string.IsNullOrEmpty(request.SpellDescription))
            Console.WriteLine($"        {request.SpellDescription}");

        int tokens = PromptCount("Tokens", request.AvailableTokens);
        int power = PromptCount("Power", request.AvailablePower);
        return Task.FromResult(new CastAssistReply(tokens, power));
    }

    // 0..max, EOF or blank spends nothing. Returns 0 without printing when there is nothing to spend, so
    // an ordinary army caster sees exactly the one token prompt it always saw.
    private static int PromptCount(string label, int max)
    {
        if (max <= 0) return 0;
        while (true)
        {
            Console.Write($"{label} [0-{max}] (default 0): ");
            string? input = Console.ReadLine()?.Trim();

            if (input == null)
            {
                Console.WriteLine("(EOF - spending none)");
                return 0;
            }
            if (string.IsNullOrEmpty(input)) return 0;
            if (int.TryParse(input, out int n) && n >= 0 && n <= max)
                return n;

            Console.WriteLine($"Enter a number between 0 and {max}.");
        }
    }
}
