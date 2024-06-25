using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using ZG;

[Serializable, EntityDataTypeName("GameChild")]
public struct GameContainerChild : IBufferElementData, IGameDataEntityCompoent
{
    public int index;
    public Entity entity;

    public void Serialize(int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(index);
        writer.Write(entityIndex);
    }

    public int Deserialize(ref EntityDataReader reader)
    {
        index = reader.Read<int>();

        entity = Entity.Null;

        return reader.Read<int>();
    }

    Entity IGameDataEntityCompoent.entity
    {
        get => entity;

        set => entity = value;
    }
}

[Serializable]
public struct GameContainerWeight : IComponentData
{
    public int value;
}

[Serializable]
public struct GameContainerBearing : IComponentData
{
    public int value;
}

public struct GameContainerStatusDisabled : IComponentData
{

}

public struct GameContainerEnumerator : IEnumerator<Entity>
{
    [Flags]
    private enum Flag
    {
        Parent = 0x01,
        Children = 0x02
    }

    private Flag __flag;
    private SharedMultiHashMap<Entity, EntityData<int>>.Enumerator __parent;
    private SharedMultiHashMap<Entity, EntityData<int>>.Enumerator __children;

    public Entity Current
    {
        get
        {
            if ((__flag & Flag.Parent) == Flag.Parent)
                return __parent.Current.entity;

            return (__flag & Flag.Children) == Flag.Children ? __children.Current.entity : Entity.Null;
        }
    }

    public GameContainerEnumerator(
        in Entity entity, 
        in SharedMultiHashMap<Entity, EntityData<int>>.Reader parent, 
        in SharedMultiHashMap<Entity, EntityData<int>>.Reader children)
    {
        __flag = (Flag)~0;

        __parent = parent.GetValuesForKey(entity);
        __children = children.GetValuesForKey(entity);
    }

    public bool MoveNext()
    {
        if ((__flag & Flag.Parent) == Flag.Parent)
        {
            if (__parent.MoveNext())
                return true;

            __flag &= ~Flag.Parent;
        }

        if ((__flag & Flag.Children) == Flag.Children)
        {
            if (__children.MoveNext())
                return true;

            __flag &= ~Flag.Children;
        }

        return false;
    }

    public void Reset()
    {
        __flag = (Flag)~0;
        __parent.Reset();
        __children.Reset();
    }

    public void Dispose()
    {
        __flag = 0;
        __parent.Dispose();
        __children.Dispose();
    }

    object IEnumerator.Current => Current;
}

public struct GameContainerHierarchyEnumerator : IEnumerator<Entity>
{
    private SharedMultiHashMap<Entity, EntityData<int>>.Reader __parentIndices;
    private SharedMultiHashMap<Entity, EntityData<int>>.Reader __childIndices;
    private UnsafeList<GameContainerEnumerator> __enumerators;
    private UnsafeHashSet<Entity> __entities;

    public Entity Current => __enumerators.Length > 0 ? __enumerators[^1].Current : Entity.Null;

    public GameContainerHierarchyEnumerator(
        in AllocatorManager.AllocatorHandle allocator, 
        in SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices, 
        in SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices)
    {
        __parentIndices = parentIndices;
        __childIndices = childIndices;

        __enumerators = new UnsafeList<GameContainerEnumerator>(1, allocator);
        
        __entities = new UnsafeHashSet<Entity>(1, allocator);
    }

    public void Dispose()
    {
        __entities.Dispose();
        
        foreach (var enumerator in __enumerators)
            enumerator.Dispose();
        
        __enumerators.Dispose();
    }

    public void Init(in Entity entity)
    {
        foreach (var enumerator in __enumerators)
            enumerator.Dispose();

        __enumerators.Clear();
        __enumerators.Add(new GameContainerEnumerator(entity, __parentIndices, __childIndices));
        
        __entities.Clear();
        __entities.Add(entity);
    }

    public bool MoveNext()
    {
        GameContainerEnumerator enumerator, childEnumerator;
        while (__enumerators.Length > 0)
        {
            enumerator = __enumerators[^1];
            childEnumerator = new GameContainerEnumerator(enumerator.Current, __parentIndices, __childIndices);
            while (childEnumerator.MoveNext())
            {
                if (__entities.Add(childEnumerator.Current))
                {
                    __enumerators.Add(childEnumerator);
                    
                    return true;
                }
            }

            if (enumerator.MoveNext())
            {
                __enumerators[^1] = enumerator;

                if (__entities.Add(enumerator.Current))
                    return true;
            }
            else
                __enumerators.RemoveAt(__enumerators.Length - 1);
        }

        return false;
    }

    void IEnumerator.Reset()
    {
        throw new NotImplementedException();
    }

    object IEnumerator.Current => Current;
}

[EntityComponent(typeof(GameContainerChild))]
public class GameContainerComponent : EntityProxyComponent
{
    private static List<GameContainerChild> __childrenTemp = null;

    /*public GameContainerEnumerator siblings
    {
        get
        {
            var gameObjectEntity = base.gameObjectEntity;

            gameObjectEntity.ExecuteAllCommands();
            ref var system = ref gameObjectEntity.world.GetExistingSystem<GameContainerChildSystem>().Struct;
            system.Update();
            //system.CompleteReadOnlyDependency();

            var parentIndices = system.parentIndices;
            parentIndices.lookupJobManager.CompleteReadOnlyDependency();
            var childIndices = system.childIndices;
            childIndices.lookupJobManager.CompleteReadOnlyDependency();

            Entity entity = gameObjectEntity.entity;

            return new GameContainerEnumerator(entity, system.parentIndices, system.childIndices);
        }
    }*/

    public bool Add(in Entity entity, int index)
    {
        if (__childrenTemp == null)
            __childrenTemp = new List<GameContainerChild>();
        else
            __childrenTemp.Clear();

        WriteOnlyListWrapper<GameContainerChild, List<GameContainerChild>> wrapper;

        gameObjectEntity.TryGetBuffer<GameContainerChild, List<GameContainerChild>, WriteOnlyListWrapper<GameContainerChild, List<GameContainerChild>>>(ref __childrenTemp, ref wrapper);
        GameContainerChild child;
        int length = __childrenTemp.Count, i;
        for (i = 0; i < length; ++i)
        {
            child = __childrenTemp[i];
            if (child.index == index || child.entity == entity)
                return false;
        }

        child.index = index;
        child.entity = entity;
        if (i < length)
        {
            __childrenTemp[i] = child;

            this.SetBuffer<GameContainerChild, List<GameContainerChild>>(__childrenTemp);
        }
        else
            this.AppendBuffer(child);

        return true;
    }

    public Entity Remove(int index)
    {
        if (__childrenTemp == null)
            __childrenTemp = new List<GameContainerChild>();
        else
            __childrenTemp.Clear();

        WriteOnlyListWrapper<GameContainerChild, List<GameContainerChild>> wrapper;
        this.TryGetBuffer<GameContainerChild, List<GameContainerChild>, WriteOnlyListWrapper<GameContainerChild, List<GameContainerChild>>>(ref __childrenTemp, ref wrapper);

        GameContainerChild child = default;
        int length = __childrenTemp.Count, i;
        for (i = 0; i < length; ++i)
        {
            child = __childrenTemp[i];
            if (child.index == index)
                break;
        }

        if (i == length)
            return Entity.Null;

        __childrenTemp.RemoveAt(i);

        this.SetBuffer<GameContainerChild, List<GameContainerChild>>(__childrenTemp);

        return child.entity;
    }

}
