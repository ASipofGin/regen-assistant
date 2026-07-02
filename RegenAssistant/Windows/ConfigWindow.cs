using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RegenAssistant.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private int newStatusId;
    private float newStatusPotency = 100f;

    public ConfigWindow(Plugin plugin) : base("Regen Assistant Settings###RegenAssistantConfig")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

        if (ImGui.CollapsingHeader("Party List Overlay", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var overlayEnabled = configuration.OverlayEnabled;
            if (ImGui.Checkbox("Enable overlay", ref overlayEnabled))
            {
                configuration.OverlayEnabled = overlayEnabled;
                changed = true;
            }

            var showHeal = configuration.ShowHealText;
            if (ImGui.Checkbox("Show estimated healing", ref showHeal))
            {
                configuration.ShowHealText = showHeal;
                changed = true;
            }

            var showTime = configuration.ShowTimeText;
            if (ImGui.Checkbox("Show time to completion", ref showTime))
            {
                configuration.ShowTimeText = showTime;
                changed = true;
            }

            var showSquare = configuration.ShowSquare;
            if (ImGui.Checkbox("Show square gauge", ref showSquare))
            {
                configuration.ShowSquare = showSquare;
                changed = true;
            }

            var offset = new Vector2(configuration.OffsetX, configuration.OffsetY);
            if (ImGui.DragFloat2("Offset from HP gauge", ref offset, 0.5f, -400f, 400f, "%.0f px"))
            {
                configuration.OffsetX = offset.X;
                configuration.OffsetY = offset.Y;
                changed = true;
            }

            var squareSize = configuration.SquareSize;
            if (ImGui.DragFloat("Square size", ref squareSize, 0.2f, 4f, 48f, "%.0f px"))
            {
                configuration.SquareSize = squareSize;
                changed = true;
            }

            var fullSeconds = configuration.SquareFullSeconds;
            if (ImGui.DragFloat("Square full at", ref fullSeconds, 0.5f, 3f, 60f, "%.0f s"))
            {
                configuration.SquareFullSeconds = fullSeconds;
                changed = true;
            }

            var textColor = configuration.TextColor;
            if (ImGui.ColorEdit4("Text color", ref textColor))
            {
                configuration.TextColor = textColor;
                changed = true;
            }

            var squareColor = configuration.SquareColor;
            if (ImGui.ColorEdit4("Square color", ref squareColor))
            {
                configuration.SquareColor = squareColor;
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("Healing Estimation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var hpPerPotency = configuration.HpPerPotency;
            if (ImGui.DragFloat("HP per potency", ref hpPerPotency, 0.25f, 1f, 200f, "%.1f"))
            {
                configuration.HpPerPotency = hpPerPotency;
                changed = true;
            }

            ImGui.TextDisabled("Healing scales with the caster's stats, which are not visible for other\n" +
                               "players. Calibrate: divide an observed tick amount by the tick's potency\n" +
                               "(e.g. a 8,750 HP Regen tick / 250 potency = 35).");

            var cap = configuration.CapAtMissingHp;
            if (ImGui.Checkbox("Cap estimate at missing HP", ref cap))
            {
                configuration.CapAtMissingHp = cap;
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("Tracked Statuses"))
        {
            changed |= DrawBuiltinStatuses();
            ImGui.Separator();
            changed |= DrawCustomStatuses();
        }

        if (changed)
            configuration.Save();
    }

    private bool DrawBuiltinStatuses()
    {
        var changed = false;
        ImGui.TextDisabled("Built-in (ids resolved from game data):");

        using var table = ImRaii.Table("##builtinStatuses", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
            new Vector2(0, 220));
        if (!table.Success)
            return false;

        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 30f);
        ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, 55f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("Potency/tick", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var info in plugin.Statuses.Builtin.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Id))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = !configuration.DisabledStatuses.Contains(info.Id);
            if (ImGui.Checkbox($"##enabled{info.Id}", ref enabled))
            {
                if (enabled)
                    configuration.DisabledStatuses.Remove(info.Id);
                else
                    configuration.DisabledStatuses.Add(info.Id);
                changed = true;
            }

            ImGui.TableNextColumn();
            ImGui.Text(info.Id.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(info.Name);
            ImGui.TableNextColumn();
            ImGui.Text(info.Potency.ToString("0"));
        }

        return changed;
    }

    private bool DrawCustomStatuses()
    {
        var changed = false;
        ImGui.TextDisabled("Custom (added by you; a matching id overrides the built-in potency):");

        uint? removeId = null;
        foreach (var (id, potency) in configuration.CustomStatuses.OrderBy(kv => kv.Key))
        {
            if (ImGui.SmallButton($"Remove##custom{id}"))
                removeId = id;
            ImGui.SameLine();
            ImGui.Text($"Status #{id} — {potency:0} potency/tick");
        }

        if (removeId is { } toRemove)
        {
            configuration.CustomStatuses.Remove(toRemove);
            changed = true;
        }

        ImGui.SetNextItemWidth(110f);
        ImGui.InputInt("Status id", ref newStatusId, 0);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        ImGui.DragFloat("Potency", ref newStatusPotency, 1f, 1f, 1000f, "%.0f");
        ImGui.SameLine();
        using (ImRaii.Disabled(newStatusId <= 0))
        {
            if (ImGui.Button("Add"))
            {
                configuration.CustomStatuses[(uint)newStatusId] = newStatusPotency;
                changed = true;
            }
        }

        return changed;
    }
}
