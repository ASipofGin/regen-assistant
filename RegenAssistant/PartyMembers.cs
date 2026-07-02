using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace RegenAssistant;

public readonly record struct PartySlot(int Index, IBattleChara Chara);

/// <summary>
/// Enumerates party members in HUD order, i.e. the order slots appear in the
/// native party list, so a slot index maps directly onto the addon's member nodes.
/// </summary>
public sealed unsafe class PartyMembers(IObjectTable objectTable, IPlayerState playerState)
{
    /// <summary>Fills <paramref name="buffer"/> with the current party in HUD order. Main thread only.</summary>
    public void Collect(List<PartySlot> buffer)
    {
        buffer.Clear();

        var agent = AgentHUD.Instance();
        if (agent == null)
            return;

        var hudMembers = agent->PartyMembers;
        var count = Math.Min((int)agent->PartyMemberCount, hudMembers.Length);
        if (count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                var entityId = hudMembers[i].EntityId;
                if (entityId is 0 or 0xE0000000)
                    continue;
                if (objectTable.SearchByEntityId(entityId) is IBattleChara chara)
                    buffer.Add(new PartySlot(i, chara));
            }
        }
        else if (playerState.IsLoaded)
        {
            // Solo: the party list still shows the local player in slot 0.
            if (objectTable.SearchByEntityId(playerState.EntityId) is IBattleChara self)
                buffer.Add(new PartySlot(0, self));
        }
    }
}
