using System;
using Unity.Entities;
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

[Serializable]
[EntityDataStream(serializerType = typeof(EntityComponentStreamSerializer<GameEffectAreaOverride>), deserializerType = typeof(EntityComponentDeserializer<GameEffectAreaOverride>))]
public struct GameEffectAreaOverride : IComponentData
{
    public int index;
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
