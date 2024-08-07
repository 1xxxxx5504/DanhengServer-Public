﻿using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Config;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Enums.Scene;
using EggLink.DanhengServer.GameServer.Game.Activity.Loaders;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Challenge;
using EggLink.DanhengServer.GameServer.Game.ChessRogue.Cell;
using EggLink.DanhengServer.GameServer.Game.Mission;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Rogue.Scene;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Scene;

public class SceneInstance
{
    #region Scene Details

    public EntityProp? GetNearestSpring(long minDistSq)
    {
        EntityProp? spring = null;
        long springDist = 0;

        foreach (var prop in HealingSprings)
        {
            var dist = Player.Data?.Pos?.GetFast2dDist(prop.Position) ?? 1000000;
            if (dist > minDistSq) continue;

            if (spring == null || dist < springDist)
            {
                spring = prop;
                springDist = dist;
            }
        }

        return spring;
    }

    #endregion

    #region Serialization

    public SceneInfo ToProto()
    {
        SceneInfo sceneInfo = new()
        {
            WorldId = (uint)(Excel.WorldID == 100 ? Player.LastWorldId : Excel.WorldID),
            GameModeType = (uint)GameModeType,
            PlaneId = (uint)PlaneId,
            FloorId = (uint)FloorId,
            EntryId = (uint)EntryId,
            SceneMissionInfo = new MissionStatusBySceneInfo(),
            DimensionId = (uint)(EntityLoader is StoryLineEntityLoader loader ? loader.DimensionId : 0),
            GameStoryLineId = (uint)(Player.StoryLineManager?.StoryLineData.CurStoryLineId ?? 0)
        };

        var playerGroupInfo = new SceneEntityGroupInfo(); // avatar group
        foreach (var avatar in AvatarInfo)
            playerGroupInfo.EntityList.Add(avatar.Value.AvatarInfo.ToSceneEntityInfo(avatar.Value.AvatarType));
        if (playerGroupInfo.EntityList.Count > 0)
        {
            if (LeaderEntityId == 0)
            {
                LeaderEntityId = AvatarInfo.Values.First().AvatarInfo.EntityId;
                sceneInfo.LeaderEntityId = (uint)LeaderEntityId;
            }
            else
            {
                sceneInfo.LeaderEntityId = (uint)LeaderEntityId;
            }
        }

        sceneInfo.EntityGroupList.Add(playerGroupInfo);

        List<SceneEntityGroupInfo> groups = []; // other groups

        // add entities to groups
        foreach (var entity in Entities)
        {
            if (entity.Value.GroupID == 0) continue;
            if (groups.FindIndex(x => x.GroupId == entity.Value.GroupID) == -1)
                groups.Add(new SceneEntityGroupInfo
                {
                    GroupId = (uint)entity.Value.GroupID
                });
            groups[groups.FindIndex(x => x.GroupId == entity.Value.GroupID)].EntityList.Add(entity.Value.ToProto());
        }

        foreach (var groupId in Groups) // Add for empty group
            if (groups.FindIndex(x => x.GroupId == groupId) == -1)
                groups.Add(new SceneEntityGroupInfo
                {
                    GroupId = (uint)groupId
                });

        foreach (var group in groups) sceneInfo.EntityGroupList.Add(group);

        // custom save data and floor saved data
        Player.SceneData!.CustomSaveData.TryGetValue(EntryId, out var data);

        if (data != null)
            foreach (var customData in data)
                sceneInfo.CustomDataList.Add(new CustomSaveData
                {
                    GroupId = (uint)customData.Key,
                    SaveData = customData.Value
                });

        Player.SceneData!.FloorSavedData.TryGetValue(FloorId, out var floorData);

        foreach (var value in FloorInfo?.SavedValues ?? [])
            if (floorData != null && floorData.TryGetValue(value.Name, out var v))
                sceneInfo.FloorSavedData[value.Name] = v;
            else
                sceneInfo.FloorSavedData[value.Name] = value.DefaultValue;

        foreach (var value in FloorInfo?.CustomValues ?? [])
            if (floorData != null && floorData.TryGetValue(value.Name, out var v))
            {
                sceneInfo.FloorSavedData[value.Name] = v;
            }
            else
            {
                _ = int.TryParse(value.DefaultValue, out var x);
                sceneInfo.FloorSavedData[value.Name] = x;
            }

        // mission
        Player.MissionManager!.OnLoadScene(sceneInfo);

        // unlock section
        if (!ConfigManager.Config.ServerOption.AutoLightSection)
        {
            Player.SceneData!.UnlockSectionIdList.TryGetValue(FloorId, out var unlockSectionList);
            if (unlockSectionList != null)
                foreach (var sectionId in unlockSectionList)
                    sceneInfo.LightenSectionList.Add((uint)sectionId);
        }
        else
        {
            for (uint i = 1; i <= 100; i++) sceneInfo.LightenSectionList.Add(i);
        }

        return sceneInfo;
    }

    #endregion

    #region Data

    public PlayerInstance Player;
    public MazePlaneExcel Excel;
    public FloorInfo? FloorInfo;
    public int FloorId;
    public int PlaneId;
    public int EntryId;

    public int LeaveEntryId;
    public int LastEntityId;
    public bool IsLoaded = false;

    public Dictionary<int, AvatarSceneInfo> AvatarInfo = [];
    public int LeaderEntityId;
    public Dictionary<int, IGameEntity> Entities = [];
    public List<int> Groups = [];
    public List<EntityProp> HealingSprings = [];

    public SceneEntityLoader? EntityLoader;

    public GameModeTypeEnum GameModeType;

    public SceneInstance(PlayerInstance player, MazePlaneExcel excel, int floorId, int entryId)
    {
        Player = player;
        Excel = excel;
        PlaneId = excel.PlaneID;
        FloorId = floorId;
        EntryId = entryId;
        LeaveEntryId = 0;

        System.Threading.Tasks.Task.Run(async () => { await SyncLineup(true, true); }).Wait();

        GameData.GetFloorInfo(PlaneId, FloorId, out FloorInfo);
        if (FloorInfo == null) return;

        GameModeType = (GameModeTypeEnum)excel.PlaneType;
        switch (Excel.PlaneType)
        {
            case PlaneTypeEnum.Rogue:
                if (Player.ChessRogueManager!.RogueInstance != null)
                {
                    EntityLoader = new ChessRogueEntityLoader(this);
                    GameModeType = GameModeTypeEnum.ChessRogue; // ChessRogue
                }
                else
                {
                    EntityLoader = new RogueEntityLoader(this, Player);
                }

                break;
            case PlaneTypeEnum.Challenge:
                EntityLoader = new ChallengeEntityLoader(this, Player);
                break;
            case PlaneTypeEnum.TrialActivity:
                EntityLoader = new TrialActivityEntityLoader(this, Player);
                break;
            default:
                if (Player.StoryLineManager?.StoryLineData.CurStoryLineId != 0)
                    EntityLoader = new StoryLineEntityLoader(this);
                else
                    EntityLoader = new SceneEntityLoader(this);
                break;
        }

        System.Threading.Tasks.Task.Run(async () => { await EntityLoader.LoadEntity(); }).Wait();

        Player.TaskManager?.SceneTaskTrigger.TriggerFloor(PlaneId, FloorId);
    }

    #endregion

    #region Scene Actions

    public async ValueTask SyncLineup(bool notSendPacket = false, bool forceSetEntityId = false)
    {
        var oldAvatarInfo = AvatarInfo.Values.ToList();
        AvatarInfo.Clear();
        var sendPacket = false;
        var addAvatar = new List<IGameEntity>();
        var removeAvatar = new List<IGameEntity>();
        foreach (var avatar in Player.LineupManager?.GetAvatarsFromCurTeam() ?? [])
        {
            avatar.AvatarInfo.PlayerData = Player.Data;
            if (forceSetEntityId && avatar.AvatarInfo.EntityId != 0)
            {
                removeAvatar.Add(new AvatarSceneInfo(new AvatarInfo
                {
                    EntityId = avatar.AvatarInfo.EntityId
                }, AvatarType.AvatarFormalType, Player));
                avatar.AvatarInfo.EntityId = 0;
                sendPacket = true;
            }

            var avatarInstance = oldAvatarInfo.Find(x => x.AvatarInfo.AvatarId == avatar.AvatarInfo.AvatarId);
            if (avatarInstance == null)
            {
                if (avatar.AvatarInfo.EntityId == 0) avatar.AvatarInfo.EntityId = ++LastEntityId;
                addAvatar.Add(avatar);
                AvatarInfo.Add(avatar.AvatarInfo.EntityId, avatar);
                sendPacket = true;
            }
            else
            {
                AvatarInfo.Add(avatarInstance.AvatarInfo.EntityId, avatarInstance);
            }
        }

        ;
        foreach (var avatar in oldAvatarInfo)
            if (AvatarInfo.Values.ToList().FindIndex(x => x.AvatarInfo.AvatarId == avatar.AvatarInfo.AvatarId) == -1)
            {
                removeAvatar.Add(new AvatarSceneInfo(new AvatarInfo
                {
                    EntityId = avatar.AvatarInfo.EntityId
                }, AvatarType.AvatarFormalType, Player));
                avatar.AvatarInfo.EntityId = 0;
                sendPacket = true;
            }

        var leaderAvatarId = Player.LineupManager?.GetCurLineup()?.LeaderAvatarId;
        var leaderAvatarSlot = Player.LineupManager?.GetCurLineup()?.BaseAvatars
            ?.FindIndex(x => x.BaseAvatarId == leaderAvatarId);
        if (leaderAvatarSlot == -1) leaderAvatarSlot = 0;
        if (AvatarInfo.Count == 0) return;
        var info = AvatarInfo.Values.ToList()[leaderAvatarSlot ?? 0];
        LeaderEntityId = info.AvatarInfo.EntityId;
        if (sendPacket && !notSendPacket)
            await Player.SendPacket(new PacketSceneGroupRefreshScNotify(addAvatar, removeAvatar));
    }

    public void SyncGroupInfo()
    {
        EntityLoader?.SyncEntity();
    }

    #endregion

    #region Entity Management

    public async ValueTask AddEntity(IGameEntity entity)
    {
        await AddEntity(entity, IsLoaded);
    }

    public async ValueTask AddEntity(IGameEntity entity, bool sendPacket)
    {
        if (entity.EntityID != 0) return;
        entity.EntityID = ++LastEntityId;

        Entities.Add(entity.EntityID, entity);
        if (sendPacket) await Player.SendPacket(new PacketSceneGroupRefreshScNotify(entity));
    }

    public async ValueTask RemoveEntity(IGameEntity monster)
    {
        await RemoveEntity(monster, IsLoaded);
    }

    public async ValueTask RemoveEntity(IGameEntity monster, bool sendPacket)
    {
        Entities.Remove(monster.EntityID);

        if (sendPacket) await Player.SendPacket(new PacketSceneGroupRefreshScNotify(null, monster));
    }

    public List<T> GetEntitiesInGroup<T>(int groupID)
    {
        List<T> entities = [];
        foreach (var entity in Entities)
            if (entity.Value.GroupID == groupID && entity.Value is T t)
                entities.Add(t);
        return entities;
    }

    #endregion
}

public class AvatarSceneInfo(AvatarInfo avatarInfo, AvatarType avatarType, PlayerInstance Player) : IGameEntity
{
    public AvatarInfo AvatarInfo = avatarInfo;
    public AvatarType AvatarType = avatarType;

    public List<SceneBuff> BuffList = [];

    public int EntityID { get; set; } = avatarInfo.EntityId;
    public int GroupID { get; set; } = 0;

    public async ValueTask AddBuff(SceneBuff buff)
    {
        var oldBuff = BuffList.Find(x => x.BuffID == buff.BuffID);
        if (oldBuff != null)
        {
            if (oldBuff.IsExpired())
            {
                BuffList.Remove(oldBuff);
                BuffList.Add(buff);
            }
            else
            {
                oldBuff.CreatedTime = Extensions.GetUnixMs();
                oldBuff.Duration = buff.Duration;

                await Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(this, oldBuff));
                return;
            }
        }

        BuffList.Add(buff);
        await Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(this, buff));
    }

    public async ValueTask ApplyBuff(BattleInstance instance)
    {
        foreach (var buff in BuffList)
        {
            if (buff.IsExpired()) continue;
            instance.Buffs.Add(new MazeBuff(buff));
        }

        await Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(this, BuffList));

        BuffList.Clear();
    }

    public SceneEntityInfo ToProto()
    {
        return AvatarInfo.ToSceneEntityInfo(AvatarType);
    }
}