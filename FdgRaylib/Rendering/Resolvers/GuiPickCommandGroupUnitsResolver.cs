using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #439 - "pick up to N friendly units for this Banner/Musician", as one ImGui panel with a Confirm
/// button (Kai, 2026-11-01: <i>"One print per banner, choose both and click confirm"</i>).
///
/// <para>
/// <b>Deliberately plain.</b> Kai's instruction with it was "keep it basic for now, since I'm going to
/// work on UI later", and by the project's own buckets the drawing code here is RENTAL - it dies in the
/// Godot port, so it gets a working list of toggles, a count, and two buttons, and not a second pass. The
/// permanent part is the decision it collects, which lives in the engine.
/// </para>
///
/// <para>
/// Declining is a real answer, not a Back button: the rule is "UP TO 2", so Skip replies with an empty
/// list rather than a cancel sentinel, and the stage treats both the same way.
/// </para>
/// </summary>
public class GuiPickCommandGroupUnitsResolver
    : IStageResolver<PickCommandGroupUnitsRequest, CancellableResult<List<DataBinding<UnitData>>>>,
        IGuiResolver
{
    private readonly object _lock = new();
    private PickCommandGroupUnitsRequest? _request;
    private TaskCompletionSource<CancellableResult<List<DataBinding<UnitData>>>>? _tcs;

    /// <summary>Indices into the request's options. Touched only on the draw thread.</summary>
    private readonly List<int> _chosen = new();

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<CancellableResult<List<DataBinding<UnitData>>>> Resolve(
        PickCommandGroupUnitsRequest request)
    {
        var tcs = new TaskCompletionSource<CancellableResult<List<DataBinding<UnitData>>>>();
        lock (_lock) { _tcs = tcs; _request = request; _chosen.Clear(); }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PickCommandGroupUnitsRequest? request;
        TaskCompletionSource<CancellableResult<List<DataBinding<UnitData>>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        const float pad = 16f;
        float rowH = ResolverPanelLayout.OptionRowHeight();
        float dw = ResolverPanelLayout.W;
        float btnW = dw - pad * 2;

        ImGui.SetNextWindowPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y),
            ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(dw, ResolverPanelLayout.H), ImGuiCond.Always);
        ImGui.Begin("##CommandGroupPicks",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar);

        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        for (int i = 0; i < request.ValidOptions.Count; i++)
        {
            bool isChosen = _chosen.Contains(i);
            bool full = _chosen.Count >= request.MaxPicks;

            // A chosen row reads as chosen; a row that can no longer be chosen reads as unavailable.
            // Colour alone is never the only signal - the label carries the marker too.
            if (isChosen)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.45f, 0.25f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.55f, 0.30f, 1f));
            }
            else if (full)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.18f, 0.20f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.18f, 0.18f, 0.20f, 1f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.24f, 0.30f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.33f, 0.42f, 1f));
            }

            string marker = isChosen ? "[x] " : "[ ] ";
            if (ImGui.Button($"{marker}{request.ValidOptions[i].Name}##cg{i}", new Vector2(btnW, rowH)))
            {
                if (isChosen) _chosen.Remove(i);
                else if (!full) _chosen.Add(i);
            }

            ImGui.PopStyleColor(2);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted($"Picked {_chosen.Count} of {request.MaxPicks}");
        ImGui.Spacing();

        bool confirm = ImGui.Button($"Confirm##cgConfirm", new Vector2(btnW, rowH));
        bool skip = ImGui.Button($"Pick none##cgSkip", new Vector2(btnW, rowH));

        ImGui.End();

        if (!confirm && !skip) return;

        var picks = new List<DataBinding<UnitData>>();
        if (confirm)
        {
            // The request's own order, not click order, so the log line reads the same way every time.
            foreach (int index in _chosen.OrderBy(i => i))
                picks.Add(request.ValidOptions[index].Option);
        }

        lock (_lock) { _request = null; _tcs = null; _chosen.Clear(); }
        tcs.SetResult(new Selected<List<DataBinding<UnitData>>>(picks));
    }
}
