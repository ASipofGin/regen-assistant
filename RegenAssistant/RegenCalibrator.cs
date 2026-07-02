using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace RegenAssistant;

/// <summary>
/// Derives HP-per-potency from observation: when a tracked regen is freshly
/// applied to a party member, the first HP increase that follows is taken as
/// the first tick. All HoTs on an actor tick together, so the sample is
/// <c>hp delta / total active tick potency</c>. A rolling median over recent
/// samples smooths out crits, incidental heals and damage taken mid-tick.
/// </summary>
public sealed class RegenCalibrator : IDisposable
{
    // A first tick lands within one tick interval of application; allow slack.
    private const double WatchWindowSeconds = 3.6;

    // Samples outside this range are corrupted (e.g. a big casted heal or a
    // near-lethal hit in the same window) and are discarded.
    private const float MinPlausible = 3f;
    private const float MaxPlausible = 300f;

    private const int MaxSamples = 32;

    private sealed class MemberState
    {
        public uint LastHp;
        public bool Watching;
        public double WatchDeadline;
        public long LastSeenFrame;
        public readonly List<uint> ActiveIds = [];
    }

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly PartyMembers partyMembers;
    private readonly RegenStatuses statuses;
    private readonly Configuration config;
    private readonly IPluginLog log;

    private readonly Dictionary<uint, MemberState> members = [];
    private readonly List<PartySlot> slots = [];
    private readonly List<float> samples = [];
    private readonly List<uint> scratchIds = [];
    private long frame;

    public int SampleCount => samples.Count;

    /// <summary>Current auto-calibrated HP-per-potency, if any sample exists (this session or persisted).</summary>
    public float? AutoValue => samples.Count > 0 ? Median() : config.AutoHpPerPotency;

    /// <summary>The value estimates should use, honoring the auto-calibrate toggle.</summary>
    public float EffectiveHpPerPotency =>
        config.AutoCalibrate && AutoValue is { } auto ? auto : config.HpPerPotency;

    public RegenCalibrator(
        IFramework framework,
        IClientState clientState,
        PartyMembers partyMembers,
        RegenStatuses statuses,
        Configuration config,
        IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.partyMembers = partyMembers;
        this.statuses = statuses;
        this.config = config;
        this.log = log;

        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }

    public void ResetSamples()
    {
        samples.Clear();
        config.AutoHpPerPotency = null;
        config.AutoSampleCount = 0;
        config.Save();
    }

    private void OnUpdate(IFramework fw)
    {
        if (!clientState.IsLoggedIn)
        {
            members.Clear();
            return;
        }

        frame++;
        var now = Environment.TickCount64 / 1000.0;

        partyMembers.Collect(slots);
        foreach (var slot in slots)
        {
            var chara = slot.Chara;
            var entityId = chara.EntityId;
            if (!members.TryGetValue(entityId, out var state))
                members[entityId] = state = new MemberState();
            state.LastSeenFrame = frame;

            // Collect the currently active tracked HoTs and their combined potency.
            scratchIds.Clear();
            var totalPotency = 0f;
            foreach (var status in chara.StatusList)
            {
                if (status == null || status.StatusId == 0)
                    continue;
                if (!statuses.TryGet(status.StatusId, config, out var def))
                    continue;
                scratchIds.Add(def.Id);
                totalPotency += def.Potency;
            }

            // A regen we weren't seeing last frame starts a watch for its first tick.
            foreach (var id in scratchIds)
            {
                if (!state.ActiveIds.Contains(id))
                {
                    state.Watching = true;
                    state.WatchDeadline = now + WatchWindowSeconds;
                    break;
                }
            }

            var currentHp = chara.CurrentHp;
            if (state.Watching)
            {
                if (now > state.WatchDeadline)
                {
                    state.Watching = false;
                }
                else if (state.LastHp > 0 && currentHp > state.LastHp)
                {
                    // First HP increase after application: treat as the first tick.
                    // At full HP the tick is truncated, so only sub-cap deltas count.
                    if (currentHp < chara.MaxHp && totalPotency > 0f)
                        AddSample(chara.Name.TextValue, currentHp - state.LastHp, totalPotency);
                    state.Watching = false;
                }
            }

            state.LastHp = currentHp;
            state.ActiveIds.Clear();
            state.ActiveIds.AddRange(scratchIds);
        }

        // Drop members that left the party or the zone.
        if (members.Count > slots.Count)
        {
            List<uint>? stale = null;
            foreach (var (id, state) in members)
            {
                if (state.LastSeenFrame != frame)
                    (stale ??= []).Add(id);
            }

            if (stale != null)
            {
                foreach (var id in stale)
                    members.Remove(id);
            }
        }
    }

    private void AddSample(string memberName, uint healed, float totalPotency)
    {
        var ratio = healed / totalPotency;
        if (ratio is < MinPlausible or > MaxPlausible)
            return;

        if (samples.Count >= MaxSamples)
            samples.RemoveAt(0);
        samples.Add(ratio);

        var median = Median();
        config.AutoHpPerPotency = median;
        config.AutoSampleCount = samples.Count;
        config.Save();

        log.Information(
            $"Regen tick on {memberName}: +{healed} HP / {totalPotency:0} potency = {ratio:0.0} HP per potency; calibration now {median:0.0} ({samples.Count} samples)");
    }

    private float Median()
    {
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5f;
    }
}
