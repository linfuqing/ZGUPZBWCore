using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameContainerChild
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityBufferSerializationSystemCore<GameContainerChild>.Serializer, GameDataEntityBufferSerializationSystemCore<GameContainerChild>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityBufferDeserializationSystemCore<GameContainerChild>.Deserializer, GameDataEntityBufferDeserializationSystemCore<GameContainerChild>.DeserializerFactory>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityBufferDeserializationSystemCore<GameContainerChild>.Build))]
//[assembly: EntityDataSerialize(typeof(GameContainerChild), typeof(GameDataContainerChildSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameContainerChild), typeof(GameDataContainerChildDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

/*[Serializable]
public struct GameChild : IBufferElementData
{
    public int index;
    public Hash128 guid;
}*/

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameContainerChild)), 
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)), 
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataContainerChildSerializationSystem : ISystem
{
    private GameDataEntityBufferSerializationSystemCore<GameContainerChild> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityBufferSerializationSystemCore<GameContainerChild>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameContainerChild), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataContainerChildDeserializationSystem : ISystem
{
    private GameDataEntityBufferDeserializationSystemCore<GameContainerChild> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityBufferDeserializationSystemCore<GameContainerChild>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}