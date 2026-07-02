using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;

namespace RegenAssistant;

public readonly record struct ActiveRegen(
    uint StatusId,
    string Name,
    float Potency,
    float RemainingSeconds,
    float EstimatedHeal);

/// <summary>Aggregated regen state for one party member. Reused between frames.</summary>
public sealed class MemberRegenInfo
{
    public string Name = string.Empty;
    public uint CurrentHp;
    public uint MaxHp;

    /// <summary>Estimated HP the active regens will still restore (capped at missing HP if configured).</summary>
    public float EstimatedHeal;

    /// <summary>Uncapped estimate.</summary>
    public float RawEstimatedHeal;

    /// <summary>Seconds until the longest active regen expires.</summary>
    public float SecondsRemaining;

    public readonly List<ActiveRegen> Actives = [];

    public void Reset()
    {
        Name = string.Empty;
        CurrentHp = MaxHp = 0;
        EstimatedHeal = RawEstimatedHeal = SecondsRemaining = 0f;
        Actives.Clear();
    }
}

/// <summary>Computes remaining regen healing for a character from its status list.</summary>
public sealed class RegenTracker(RegenStatuses statuses, Configuration config, RegenCalibrator calibrator)
{
    /// <summary>
    /// Fills <paramref name="info"/> from the character's active statuses.
    /// Returns false when no tracked regen is active.
    /// </summary>
    public bool Compute(IBattleChara chara, MemberRegenInfo info)
    {
        info.Reset();
        info.Name = chara.Name.TextValue;
        info.CurrentHp = chara.CurrentHp;
        info.MaxHp = chara.MaxHp;

        var hpPerPotency = calibrator.EffectiveHpPerPotency;
        foreach (var status in chara.StatusList)
        {
            if (status == null || status.StatusId == 0)
                continue;
            if (!statuses.TryGet(status.StatusId, config, out var def))
                continue;

            var remaining = status.RemainingTime;
            if (remaining <= 0f)
                continue;

            // The actor tick phase is unknown, so round to the expected tick count.
            var ticks = MathF.Floor(remaining / RegenStatuses.TickIntervalSeconds + 0.5f);
            if (ticks <= 0f)
                continue;

            var heal = ticks * def.Potency * hpPerPotency;
            info.Actives.Add(new ActiveRegen(def.Id, def.Name, def.Potency, remaining, heal));
            info.RawEstimatedHeal += heal;
            info.SecondsRemaining = MathF.Max(info.SecondsRemaining, remaining);
        }

        if (info.Actives.Count == 0)
            return false;

        info.EstimatedHeal = config.CapAtMissingHp
            ? MathF.Min(info.RawEstimatedHeal, MathF.Max(0f, (float)info.MaxHp - info.CurrentHp))
            : info.RawEstimatedHeal;
        return true;
    }
}
