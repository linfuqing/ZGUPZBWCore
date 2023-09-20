using System;
using Unity.Mathematics;
using Unity.Entities;
using ZG;
using Unity.Collections;
using System.Collections.Generic;

public struct GameValhallaExp : IComponentData
{
    public float value;
}

public struct GameValhallaCollectCommand : IBufferElementData, IEnableableComponent
{
    public GameItemHandle handle;
}

public struct GameValhallaUpgradeCommand : IComponentData, IEnableableComponent
{
    public int soulIndex;
    public float exp;
    public Entity entity;
}

public struct GameValhallaRenameCommand : IComponentData, IEnableableComponent
{
    public int soulIndex;
    public Entity entity;
    public FixedString32Bytes name;
}

public struct GameValhallaDestroyCommand : IComponentData, IEnableableComponent
{
    public int soulIndex;
    public Entity entity;
}

public struct GameValhallaEvoluteCommand : IComponentData, IEnableableComponent
{
    public int soulIndex;
    public Entity entity;
}

public struct GameValhallaRespawnCommand : IComponentData, IEnableableComponent
{
    public int soulIndex;
    public float time;
    public Entity entity;
    public RigidTransform transform;
}

public struct GameValhallaSacrificer : IBufferElementData
{
    public int soulIndex;
}

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
[EntityComponent(typeof(GameValhallaCollectCommand))]
[EntityComponent(typeof(GameValhallaUpgradeCommand))]
[EntityComponent(typeof(GameValhallaRespawnCommand))]
[EntityComponent(typeof(GameValhallaDestroyCommand))]
[EntityComponent(typeof(GameValhallaEvoluteCommand))]
[EntityComponent(typeof(GameValhallaRenameCommand))]
[EntityComponent(typeof(GameValhallaSacrificer))]
public class GameValhallaComponent : EntityProxyComponent, IEntityComponent
{
    public struct RespawnData
    {
        public float respawnTime;
        public float3 respawnOffset;
    }

    [UnityEngine.SerializeField, 
        UnityEngine.Serialization.FormerlySerializedAs("_value")]
    internal RespawnData _respawnData;

    public float exp => this.GetComponentData<GameValhallaExp>().value;

    public void Collect()
    {
        this.SetBuffer<GameValhallaCollectCommand>();
        this.SetComponentEnabled<GameValhallaCollectCommand>(true);
    }

    public void Collect<T>(in T commands) where T : IReadOnlyCollection<GameValhallaCollectCommand>
    {
        this.SetBuffer<GameValhallaCollectCommand, T>(commands);
        this.SetComponentEnabled<GameValhallaCollectCommand>(true);
    }

    public void Destroy(int soulIndex, in Entity entity)
    {
        GameValhallaDestroyCommand command;
        command.soulIndex = soulIndex;
        command.entity = entity;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameValhallaDestroyCommand>(true);
    }

    public void Upgrade(int soulIndex, float exp, in Entity entity)
    {
        GameValhallaUpgradeCommand command;
        command.entity = entity;
        command.exp = exp;
        command.soulIndex = soulIndex;

        //command.version = version;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameValhallaUpgradeCommand>(true);
    }

    public void Rename(int soulIndex, in Entity entity, in FixedString32Bytes name)
    {
        GameValhallaRenameCommand command;
        command.name = name;
        command.entity = entity;
        command.soulIndex = soulIndex;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameValhallaRenameCommand>(true);
    }

    public void Evolute<T>(int soulIndex, in Entity entity, in T sacrificerIndices) where T : IReadOnlyCollection<GameValhallaSacrificer>
    {
        GameValhallaEvoluteCommand command;
        command.entity = entity;
        command.soulIndex = soulIndex;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameValhallaEvoluteCommand>(true);

        this.SetBuffer<GameValhallaSacrificer, T>(sacrificerIndices);
    }

    public void Respwan(int soulIndex, float respawnTime, in Entity entity, in RigidTransform transform)
    {
        GameValhallaRespawnCommand command;
        command.soulIndex = soulIndex;
        command.time = respawnTime;
        command.entity = entity;
        command.transform = transform;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameValhallaRespawnCommand>(true);
    }

    public void Respwan(int soulIndex, in Entity entity)
    {
        var transform = math.RigidTransform(base.transform.rotation, base.transform.position);

        transform.pos = math.transform(transform, _respawnData.respawnOffset);
        Respwan(soulIndex, _respawnData.respawnTime, entity, transform);
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        assigner.SetComponentEnabled<GameValhallaCollectCommand>(entity, false);
        assigner.SetComponentEnabled<GameValhallaUpgradeCommand>(entity, false);
        assigner.SetComponentEnabled<GameValhallaRenameCommand>(entity, false);
        assigner.SetComponentEnabled<GameValhallaDestroyCommand>(entity, false);
        assigner.SetComponentEnabled<GameValhallaEvoluteCommand>(entity, false);
        assigner.SetComponentEnabled<GameValhallaRespawnCommand>(entity, false);
    }
}
