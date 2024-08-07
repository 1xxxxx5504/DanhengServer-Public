﻿using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.RogueCommon;

public class PacketSyncRogueCommonActionResultScNotify : BasePacket
{
    public PacketSyncRogueCommonActionResultScNotify(int rogueSubmode, RogueCommonActionResult result,
        RogueCommonActionResultDisplayType displayType = RogueCommonActionResultDisplayType.None) : base(
        CmdIds.SyncRogueCommonActionResultScNotify)
    {
        var proto = new SyncRogueCommonActionResultScNotify
        {
            RogueSubMode = (uint)rogueSubmode,
            DisplayType = displayType
        };

        proto.ActionResult.Add(result);

        SetData(proto);
    }

    public PacketSyncRogueCommonActionResultScNotify(int rogueSubmode, List<RogueCommonActionResult> results,
        RogueCommonActionResultDisplayType displayType = RogueCommonActionResultDisplayType.None) : base(
        CmdIds.SyncRogueCommonActionResultScNotify)
    {
        var proto = new SyncRogueCommonActionResultScNotify
        {
            RogueSubMode = (uint)rogueSubmode,
            DisplayType = displayType
        };

        proto.ActionResult.AddRange(results);

        SetData(proto);
    }
}