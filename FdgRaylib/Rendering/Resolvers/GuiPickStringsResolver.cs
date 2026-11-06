using System.Numerics;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #462 - pick several from a list: a scrolling column of toggles (hover one for its description), a count,
/// "Use suggested" and Confirm. Plain on purpose - RENTAL drawing code; the decision lives in the engine.
/// </summary>
public class GuiPickStringsResolver : IStageResolver<PickStringsRequest, List<string>>, IGuiResolver
{
    private readonly object _lock = new();
    private PickStringsRequest? _request;
    private TaskCompletionSource<List<string>>? _tcs;
    private readonly List<string> _chosen = new();

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<List<string>> Resolve(PickStringsRequest request)
    {
        var tcs = new TaskCompletionSource<List<string>>();
        lock (_lock)
        {
            _tcs = tcs;
            _request = request;
            _chosen.Clear();
            _chosen.AddRange(request.Suggested);
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PickStringsRequest? request;
        TaskCompletionSource<List<string>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        const float pad = 16f;
        float rowH = ResolverPanelLayout.OptionRowHeight();
        float dw = ResolverPanelLayout.W;
        float btnW = dw - pad * 2;

        ImGui.SetNextWindowPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(dw, ResolverPanelLayout.H), ImGuiCond.Always);
        ImGui.Begin("##PickStrings", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        float listH = Math.Max(rowH * 3, ImGui.GetContentRegionAvail().Y - rowH * 4.5f);
        ImGui.BeginChild("##pickstringslist", new Vector2(0, listH));
        bool full = _chosen.Count >= request.MaxPicks;
        for (int i = 0; i < request.Options.Count; i++)
        {
            string option = request.Options[i];
            bool isChosen = _chosen.Contains(option);
            Vector4 colour = isChosen ? new Vector4(0.20f, 0.45f, 0.25f, 1f)
                : full ? new Vector4(0.18f, 0.18f, 0.20f, 1f) : new Vector4(0.22f, 0.24f, 0.30f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, colour);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colour * 1.2f);
            if (ImGui.Button($"{(isChosen ? "[x] " : "[ ] ")}{option}##ps{i}", new Vector2(btnW - 12f, rowH)))
            {
                if (isChosen) _chosen.Remove(option);
                else if (!full) _chosen.Add(option);
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered() && request.Descriptions != null
                && request.Descriptions.TryGetValue(option, out string? description) && description.Length > 0)
                UiText.Tooltip(description);
        }
        ImGui.EndChild();

        string bounds = request.MinPicks == request.MaxPicks ? $"{request.MaxPicks}" : $"{request.MinPicks}-{request.MaxPicks}";
        ImGui.TextUnformatted($"Picked {_chosen.Count} (pick {bounds})");

        if (ImGui.Button("Use suggested##psSuggest", new Vector2(btnW, rowH)))
        {
            _chosen.Clear();
            _chosen.AddRange(request.Suggested);
        }
        var picks = request.Options.Where(o => _chosen.Contains(o)).ToList();
        bool legal = request.Legal(picks);
        ImGui.BeginDisabled(!legal);
        bool confirm = ImGui.Button("Confirm##psConfirm", new Vector2(btnW, rowH));
        ImGui.EndDisabled();
        ImGui.End();

        if (!confirm || !legal) return;
        lock (_lock) { _request = null; _tcs = null; _chosen.Clear(); }
        tcs.SetResult(picks);
    }
}
