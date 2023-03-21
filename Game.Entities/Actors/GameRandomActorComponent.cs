using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using ZG;

[Serializable]
public struct GameRandomActorSlice : IBufferElementData
{
    public int groupStartIndex;
    public int groupCount;
}

[Serializable]
public struct GameRandomActorGroup : IBufferElementData
{
    public RandomGroup value;
}

[Serializable]
public struct GameRandomActorAction : IBufferElementData
{
    public int index;
}

[Serializable]
public struct GameRandomActorNode : IBufferElementData
{
    public int sliceIndex;
}

[EntityComponent(typeof(GameRandomActorSlice))]
[EntityComponent(typeof(GameRandomActorGroup))]
[EntityComponent(typeof(GameRandomActorAction))]
[EntityComponent(typeof(GameRandomActorNode))]
public class GameRandomActorComponent : EntityProxyComponent, IEntityComponent
{
    [Serializable]
    public struct Slice
    {
#if UNITY_EDITOR
        public string name;
#endif
        public RandomGroup[] groups;
    }

    [SerializeField]
    internal Slice[] _slices = null;
    
    [SerializeField]
    internal int[] _actionIndices = null;
    
    private static List<GameRandomActorGroup> __groups = null;
    
    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        int length = _actionIndices == null ? 0 : _actionIndices.Length;
        if (length > 0)
        {
            GameRandomActorAction[] acions = new GameRandomActorAction[length];
            for (int i = 0; i < length; ++i)
                acions[i].index = _actionIndices[i];

            assigner.SetBuffer(true, entity, acions);
        }

        length = _slices == null ? 0 : _slices.Length;
        if (length > 0)
        {
            if (__groups == null)
                __groups = new List<GameRandomActorGroup>(length);
            else
                __groups.Clear();

            GameRandomActorGroup destinationGroup;
            foreach (Slice slice in _slices)
            {
                foreach (var sourceGroup in slice.groups)
                {
                    destinationGroup.value = sourceGroup;
                    __groups.Add(destinationGroup);
                }
            }

            if(__groups.Count > 0)
                assigner.SetBuffer(true, entity, __groups.ToArray());

            GameRandomActorSlice[] slices = new GameRandomActorSlice[length];

            int groupCount = 0;
            Slice sourceSlice;
            GameRandomActorSlice destinationSlice;
            for(int i = 0; i < length; ++i)
            {
                sourceSlice = _slices[i];

                destinationSlice.groupStartIndex = groupCount;
                destinationSlice.groupCount = sourceSlice.groups.Length;

                slices[i] = destinationSlice;

                groupCount += destinationSlice.groupCount;
            }

            assigner.SetBuffer(true, entity, slices);
        }
    }
}
