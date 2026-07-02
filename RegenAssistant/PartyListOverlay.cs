using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace RegenAssistant;

/// <summary>
/// Draws remaining-regen info (estimated HP, time to completion and a square
/// gauge) beside each member's HP gauge on the native party list.
/// </summary>
public sealed unsafe class PartyListOverlay(
    IGameGui gameGui,
    IClientState clientState,
    PartyMembers partyMembers,
    RegenTracker tracker,
    Configuration config)
{
    private readonly List<PartySlot> slots = [];
    private readonly MemberRegenInfo scratch = new();

    public void Draw()
    {
        if (!config.OverlayEnabled || (!config.ShowHealText && !config.ShowTimeText && !config.ShowSquare))
            return;
        if (!clientState.IsLoggedIn)
            return;

        var addonPtr = gameGui.GetAddonByName("_PartyList");
        if (addonPtr.IsNull || !addonPtr.IsReady || !addonPtr.IsVisible)
            return;

        var addon = (AddonPartyList*)addonPtr.Address;
        var scale = addonPtr.Scale;
        var drawList = ImGui.GetBackgroundDrawList();
        var memberNodes = addon->PartyMembers;

        partyMembers.Collect(slots);
        foreach (var slot in slots)
        {
            if (slot.Index >= addon->MemberCount || slot.Index >= memberNodes.Length)
                continue;
            if (!tracker.Compute(slot.Chara, scratch))
                continue;

            var gauge = memberNodes[slot.Index].HPGaugeComponent;
            if (gauge == null || gauge->OwnerNode == null)
                continue;

            var node = &gauge->OwnerNode->AtkResNode;
            var right = node->ScreenX + (node->Width * scale);
            var centerY = node->ScreenY + (node->Height * scale * 0.5f);
            DrawMemberInfo(drawList, new Vector2(right, centerY), scale, scratch);
        }
    }

    private void DrawMemberInfo(ImDrawListPtr drawList, Vector2 anchor, float scale, MemberRegenInfo info)
    {
        var x = anchor.X + (config.OffsetX * scale);
        var y = anchor.Y + (config.OffsetY * scale);

        if (config.ShowSquare)
        {
            var size = config.SquareSize * scale;
            var min = new Vector2(x, y - (size * 0.5f));
            var max = min + new Vector2(size, size);

            // Drain from the top as the longest regen runs out.
            var fill = Math.Clamp(info.SecondsRemaining / MathF.Max(1f, config.SquareFullSeconds), 0f, 1f);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)));
            drawList.AddRectFilled(new Vector2(min.X, max.Y - (size * fill)), max, ImGui.GetColorU32(config.SquareColor));
            drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.9f)));

            x = max.X + (4f * scale);
        }

        var text = BuildText(info);
        if (text.Length == 0)
            return;

        var textSize = ImGui.CalcTextSize(text);
        var pos = new Vector2(x, y - (textSize.Y * 0.5f));

        // Cheap outline so the text stays readable over the game UI.
        var outline = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f));
        drawList.AddText(pos + new Vector2(1f, 0f), outline, text);
        drawList.AddText(pos + new Vector2(-1f, 0f), outline, text);
        drawList.AddText(pos + new Vector2(0f, 1f), outline, text);
        drawList.AddText(pos + new Vector2(0f, -1f), outline, text);
        drawList.AddText(pos, ImGui.GetColorU32(config.TextColor), text);
    }

    private string BuildText(MemberRegenInfo info)
    {
        var text = string.Empty;
        if (config.ShowHealText)
            text = $"+{FormatHp(info.EstimatedHeal)}";
        if (config.ShowTimeText)
            text += text.Length > 0 ? $" {info.SecondsRemaining:0}s" : $"{info.SecondsRemaining:0}s";
        return text;
    }

    public static string FormatHp(float value) => value switch
    {
        >= 100_000f => $"{value / 1000f:0}k",
        >= 10_000f => $"{value / 1000f:0.0}k",
        _ => $"{value:0}",
    };
}
