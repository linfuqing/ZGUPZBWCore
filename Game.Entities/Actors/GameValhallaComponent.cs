using System;
using Unity.Mathematics;
using Unity.Entities;
using ZG;
using Unity.Collections;
using System.Collections.Generic;

[Serializable]
public struct GameValhallaData : IComponentData
{
    public float respawnTime;
    public float3 respawnOffset;
}

[Serializable]
public struct GameValhallaExp : IComponentData
{
    public float value;
}

[Serializable]
public struct GameValhallaVersion : IComponentData
{
    public int value;
}

[Serializable]
public struct GameValhallaCollectCommand : IComponentData
{
    public int version;
}

[Serializable]
public struct GameValhallaUpgradeCommand : IComponentData
{
    public int version;
    public int soulIndex;
    public float exp;
    public Entity entity;
}

[Serializable]
public struct GameValhallaRenameCommand : IComponentData
{
    public int version;
    public int soulIndex;
    public Entity entity;
    public FixedString32Bytes name;
}

[Serializable]
public struct GameValhallaDestroyCommand : IComponentData
{
    public int version;
    public int soulIndex;
    public Entity entity;
}

[Serializable]
public struct GameValhallaEvoluteCommand : IComponentData
{
    public int version;
    public int soulIndex;
    public Entity entity;
}

[Serializable]
public struct GameValhallaRespawnCommand : IComponentData
{
    public int version;
    public int soulIndex;
    public Entity entity;
}

[Serializable]
public struct GameValhallaSacrificer : IBufferElementData
{
    public int soulIndex;
}

[Serializable]
public struct GameValhallaCommand
{
    public int variant;
    public int type;
    public Entity entity;
    public RigidTransform transform;
    public FixedString32Bytes nickname;
    public GameItemHandle itemHandle;
    //public GameSoulData value;
}

[EntityComponent(typeof(GameValhallaExp))]
[EntityComponent(typeof(GameValhallaVersion))]
[EntityComponent(typeof(GameValhallaCollectCommand))]
[EntityComponent(typeof(GameValhallaUpgradeCommand))]
[EntityComponent(typeof(GameValhallaRespawnCommand))]
[EntityComponent(typeof(GameValhallaDestroyCommand))]
[EntityComponent(typeof(GameValhallaEvoluteCommand))]
[EntityComponent(typeof(GameValhallaRenameCommand))]
[EntityComponent(typeof(GameValhallaSacrificer))]
public class GameValhallaComponent : ComponentDataProxy<GameValhallaData>
{
    public int version => this.GetComponentData<GameValhallaVersion>().value;

    public float exp => this.GetComponentData<GameValhallaExp>().value;

    public void Collect()
    {
        GameValhallaCollectCommand command;
        command.version = version;
        gameObjectEntity.SetComponentData(command);
    }

    public void Upgrade(int soulIndex, float exp, in Entity entity)
    {
        GameValhallaUpgradeCommand command;
        command.entity = entity;
        command.exp = exp;
        command.soulIndex = soulIndex;

        command.version = version;

        this.SetComponentData(command);
    }

    public void Rename(int soulIndex, in Entity entity, in FixedString32Bytes name)
    {
        GameValhallaRenameCommand command;
        command.name = name;
        command.entity = entity;
        command.soulIndex = soulIndex;

        command.version = version;

        this.SetComponentData(command);
    }

    public void Destroy(int soulIndex, in Entity entity)
    {
        GameValhallaDestroyCommand command;
        command.soulIndex = soulIndex;
        command.entity = entity;

        command.version = version;

        this.SetComponentData(command);
    }

    public void Evolute<T>(int soulIndex, in Entity entity, in T sacrificerIndices) where T : IReadOnlyCollection<GameValhallaSacrificer>
    {
        GameValhallaEvoluteCommand command;
        command.entity = entity;
        command.soulIndex = soulIndex;

        command.version = version;

        this.SetComponentData(command);

        this.SetBuffer<GameValhallaSacrificer, T>(sacrificerIndices);
    }

    public void Respwan(int soulIndex, in Entity entity)
    {
        GameValhallaRespawnCommand command;
        command.entity = entity;
        command.soulIndex = soulIndex;

        command.version = version;

        this.SetComponentData(command);
    }
}
