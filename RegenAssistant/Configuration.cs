using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RegenAssistant;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Party list overlay
    public bool OverlayEnabled { get; set; } = true;
    public bool ShowHealText { get; set; } = true;
    public bool ShowTimeText { get; set; } = true;
    public bool ShowSquare { get; set; } = true;
    public float OffsetX { get; set; } = 6f;
    public float OffsetY { get; set; } = 0f;
    public float SquareSize { get; set; } = 14f;

    // Seconds of remaining regen that renders the square as completely full.
    public float SquareFullSeconds { get; set; } = 21f;

    public Vector4 TextColor { get; set; } = new(0.55f, 1f, 0.6f, 1f);
    public Vector4 SquareColor { get; set; } = new(0.3f, 0.9f, 0.4f, 0.9f);

    // Estimation
    // HP restored per point of cure potency. Depends on the caster's stats, so
    // it is a calibration knob: observed tick amount / tick potency.
    public float HpPerPotency { get; set; } = 35f;
    public bool CapAtMissingHp { get; set; } = false;

    // Status id -> potency per tick. Overrides built-ins and adds unknown HoTs.
    public Dictionary<uint, float> CustomStatuses { get; set; } = new();

    // Built-in status ids the user turned off.
    public HashSet<uint> DisabledStatuses { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
