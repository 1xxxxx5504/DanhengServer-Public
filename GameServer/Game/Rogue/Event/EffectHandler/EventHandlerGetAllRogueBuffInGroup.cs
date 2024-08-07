﻿using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Enums.Rogue;

namespace EggLink.DanhengServer.GameServer.Game.Rogue.Event.EffectHandler;

[RogueEvent(DialogueEventTypeEnum.GetAllRogueBuffInGroup)]
public class EventHandlerGetAllRogueBuffInGroup : RogueEventEffectHandler
{
    public override async ValueTask Handle(BaseRogueInstance rogue, RogueEventInstance? eventInstance,
        List<int> paramList)
    {
        var group = paramList[0];
        GameData.RogueBuffGroupData.TryGetValue(group, out var buffGroup);
        if (buffGroup == null) return;
        await rogue.AddBuffList(buffGroup.BuffList);
    }
}