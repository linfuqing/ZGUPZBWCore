using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using ZG;

[assembly: RegisterGenericComponentType(typeof(GameEffectData<GameEffect>))]
[assembly: RegisterGenericComponentType(typeof(GameEffectResult<GameEffect>))]

public interface IGameEffect<T> where T : struct, IGameEffect<T>
{
    void Add(in T value);
}

[Serializable]
public struct GameEffectData<T> : IComponentData where T : struct, IGameEffect<T>
{
    public T value;
}

[Serializable]
public struct GameEffectResult<T> : IComponentData where T : struct, IGameEffect<T>
{
    public T value;
}

[Serializable]
public struct GameEffectArea : IComponentData
{
    public int index;
}

[EntityDataStream(serializerType = typeof(EntityComponentStreamSerializer<GameEffectAreaOverride>), deserializerType = typeof(GameEffectAreaOverrideDeserializer))]
public struct GameEffectAreaOverride : IComponentData
{
    public int index;
}

public struct GameEffectAreaOverrideBuffer : IBufferElementData
{
    public int index;
    public ColliderKey colliderKey;
}

public struct GameEffectAreaOverrideDeserializer : IEntityDataStreamDeserializer
{
    public ComponentTypeSet GetComponentTypeSet(in NativeArray<byte> userData)
    {
        return new ComponentTypeSet(userData.IsCreated ? ComponentType.ReadWrite<GameEffectAreaOverrideBuffer>() : ComponentType.ReadWrite<GameEffectAreaOverride>());
    }

    public void Deserialize(ref UnsafeBlock.Reader reader, ref EntityComponentAssigner assigner, in Entity entity, in NativeArray<byte> userData)
    {
        var value = reader.Read<GameEffectAreaOverride>();
        GameEffectAreaOverrideBuffer buffer;
        buffer.index = value.index;
        buffer.colliderKey = userData.IsCreated ? userData.Reinterpret<ColliderKey>(1)[0] : ColliderKey.Empty;
        if (assigner.isCreated)
            assigner.SetBuffer(EntityComponentAssigner.BufferOption.AppendUnique,  entity, buffer);
    }
}

[EntityComponent(typeof(PhysicsTriggerEvent))]
[EntityComponent(typeof(GameEffectArea))]
public class GameEffectComponent<T> : EntityProxyComponent, IEntityComponent where T : unmanaged, IGameEffect<T>
{
    [UnityEngine.Serialization.FormerlySerializedAs("_effect")]
    [UnityEngine.SerializeField]
    internal T _value = default;
    
    public int areaIndex
    {
        get
        {
            return this.HasComponent<Disabled>() ? -1 : this.GetComponentData<GameEffectArea>().index;
        }
    }

    public T value => this.GetComponentData<GameEffectResult<T>>().value;

    public void Set(T value)
    {
        _value.Add(value);

        GameEffectData<T> result;
        result.value = _value;
        this.SetComponentData(result);
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEffectArea area;
        area.index = -1;
        assigner.SetComponentData(entity, area);
        
        GameEffectData<T> result;
        result.value = _value;
        assigner.SetComponentData(entity, result);
    }
}

[EntityComponent(typeof(GameEffectData<GameEffect>))]
[EntityComponent(typeof(GameEffectResult<GameEffect>))]
public class GameEffectComponent : GameEffectComponent<GameEffect>
{
}
