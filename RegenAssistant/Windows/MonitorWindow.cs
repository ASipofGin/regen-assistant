using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RegenAssistant.Windows;

/// <summary>Standalone list of every party member's active regens.</summary>
public class MonitorWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly List<PartySlot> slots = [];
    private readonly MemberRegenInfo scratch = new();

    public MonitorWindow(Plugin plugin) : base("Party Regen Monitor###RegenAssistantMonitor")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 160),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!Plugin.ClientState.IsLoggedIn)
        {
            ImGui.Text("Not logged in.");
            return;
        }

        plugin.PartyMembers.Collect(slots);
        if (slots.Count == 0)
        {
            ImGui.Text("No party members found.");
            return;
        }

        using var table = ImRaii.Table("##regenMonitor", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Member", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Regen", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Completes", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableHeadersRow();

        foreach (var slot in slots)
        {
            var hasRegen = plugin.Tracker.Compute(slot.Chara, scratch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(scratch.Name);

            ImGui.TableNextColumn();
            ImGui.Text($"{scratch.CurrentHp:N0} / {scratch.MaxHp:N0}");

            ImGui.TableNextColumn();
            if (hasRegen)
            {
                ImGui.TextColored(plugin.Configuration.TextColor, $"+{PartyListOverlay.FormatHp(scratch.EstimatedHeal)}");
                if (ImGui.IsItemHovered())
                    DrawBreakdownTooltip(scratch);
            }
            else
            {
                ImGui.TextDisabled("—");
            }

            ImGui.TableNextColumn();
            if (hasRegen)
            {
                var fraction = Math.Clamp(scratch.SecondsRemaining / MathF.Max(1f, plugin.Configuration.SquareFullSeconds), 0f, 1f);
                ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"{scratch.SecondsRemaining:0.0}s");
            }
            else
            {
                ImGui.TextDisabled("—");
            }
        }
    }

    private static void DrawBreakdownTooltip(MemberRegenInfo info)
    {
        using var tooltip = ImRaii.Tooltip();
        foreach (var active in info.Actives)
        {
            ImGui.Text($"{active.Name} ({active.Potency:0} potency/tick)");
            ImGui.SameLine();
            ImGui.TextDisabled($"+{PartyListOverlay.FormatHp(active.EstimatedHeal)} over {active.RemainingSeconds:0.0}s");
        }

        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextDisabled($"Total: +{PartyListOverlay.FormatHp(info.RawEstimatedHeal)}");
    }
}
