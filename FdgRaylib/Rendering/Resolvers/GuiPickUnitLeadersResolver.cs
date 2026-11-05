using System.Numerics;
using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #456 - Fast-Play unit leaders, confirmed before the game: every unit with a choice, each with a drop-down
/// of its models pre-set to the game's suggestion, and one Confirm (Kai, 2026-11-05: "Game picks, I can
/// change it"). With a big army, Confirm without touching anything is the whole interaction.
///
/// <para>Plain on purpose - the drawing is RENTAL under the working agreement; the leaders it records are
/// engine state.</para>
/// </summary>
public class GuiPickUnitLeadersResolver
    : IStageResolver<PickUnitLeadersRequest, CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>,
        IGuiResolver
{
    private readonly object _lock = new();
    private PickUnitLeadersRequest? _request;
    private TaskCompletionSource<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>? _tcs;

    /// <summary>The chosen model index per unit entry. Touched only on the draw thread after Resolve.</summary>
    private int[] _choice = Array.Empty<int>();

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>> Resolve(PickUnitLeadersRequest request)
    {
        var tcs = new TaskCompletionSource<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>();
        int[] choice = request.Units
            .Select(u => Math.Max(0, u.Models.ToList().FindIndex(m => m.Model.Equals(u.Suggested))))
            .ToArray();
        lock (_lock) { _tcs = tcs; _request = request; _choice = choice; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PickUnitLeadersRequest? request;
        TaskCompletionSource<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>? tcs;
        int[] choice;
        lock (_lock) { request = _request; tcs = _tcs; choice = _choice; }
        if (request == null || tcs == null) return;

        const float pad = 16f;
        float rowH = ResolverPanelLayout.OptionRowHeight();
        float dw = ResolverPanelLayout.W;
        float btnW = dw - pad * 2;

        ImGui.SetNextWindowPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(dw, ResolverPanelLayout.H), ImGuiCond.Always);
        ImGui.Begin("##UnitLeaders",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar);

        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted("UNIT LEADERS");
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();
        ImGui.Separator();

        float listH = ImGui.GetContentRegionAvail().Y - rowH - ImGui.GetStyle().ItemSpacing.Y * 3f;
        ImGui.BeginChild("##leaderList", new Vector2(0f, Math.Max(rowH * 3f, listH)));
        for (int i = 0; i < request.Units.Count; i++)
        {
            PickUnitLeadersRequest.UnitEntry unit = request.Units[i];
            ImGui.TextUnformatted(unit.UnitName);
            string[] labels = unit.Models
                .Select(m => m.Model.Equals(unit.Suggested) ? m.Description + "  (suggested)" : m.Description)
                .ToArray();
            ImGui.SetNextItemWidth(-1f);
            int c = choice[i];
            if (ImGui.Combo($"##leader{i}", ref c, labels, labels.Length)) choice[i] = c;
            ImGui.Spacing();
        }
        ImGui.EndChild();

        bool confirm = ImGui.Button("Confirm leaders##leadersConfirm", new Vector2(btnW, rowH));
        ImGui.End();
        if (!confirm) return;

        var picks = request.Units
            .Select((u, i) => new PickUnitLeadersRequest.LeaderPick(u.Unit, u.Models[Math.Clamp(choice[i], 0, u.Models.Count - 1)].Model))
            .ToList();
        lock (_lock) { _request = null; _tcs = null; _choice = Array.Empty<int>(); }
        tcs.SetResult(new Selected<List<PickUnitLeadersRequest.LeaderPick>>(picks));
    }
}
