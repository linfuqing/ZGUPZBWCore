using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using ZG;
using FixedString = Unity.Collections.FixedString32Bytes;
using BitField = ZG.BitField<Unity.Collections.FixedBytes30>;

public struct GamePhysicsHierarchyDefinition
{
    public struct Shape
    {
        public FixedString name;

        //��Ϊ����ʱ�Ӽ��ṹӦ��ɾ���������㡣
        public BlobArray<FixedString> childShapeNames;

        //��ǰ�ṹӦ��ɾ���������㡣"
        public BlobArray<int> shapeIndices;
    }

    public BlobArray<Shape> shapes;
    public BlobArray<int> childShapeIndices;

    public static void AddChild(
        int index, 
        ref GamePhysicsHierarchyDefinition sourceDefinition, 
        ref GamePhysicsHierarchyDefinition destinationDefinition, 
        ref DynamicBuffer<PhysicsHierarchyInactiveTriggers> sourceInactiveTriggers,
        ref DynamicBuffer<PhysicsHierarchyInactiveTriggers> destinationInactiveTriggers)
    {
        UnityEngine.Assertions.Assert.IsTrue(index >= 0 && index < sourceDefinition.childShapeIndices.Length);

        int shapeShapeIndex = sourceDefinition.childShapeIndices[index];

        UnityEngine.Assertions.Assert.IsTrue(shapeShapeIndex >= 0 && shapeShapeIndex < sourceDefinition.shapes.Length);

        ref var sourceShape = ref sourceDefinition.shapes[shapeShapeIndex];
        sourceInactiveTriggers.Reinterpret<int>().AddRange(sourceShape.shapeIndices.AsArray());

        PhysicsHierarchyInactiveTriggers inactiveTriggers;
        int i, j, numSourceChildShapeNames = sourceShape.childShapeNames.Length, numDestinationShapes = destinationDefinition.shapes.Length;
        for(i = 0; i < numSourceChildShapeNames; ++i)
        {
            ref var childShpaeName = ref sourceShape.childShapeNames[i];
            for(j = 0; j < numDestinationShapes; ++j)
            {
                ref var destinationShape = ref destinationDefinition.shapes[j];
                if(destinationShape.name == childShpaeName)
                {
                    inactiveTriggers.shapeIndex = j;
                    destinationInactiveTriggers.Add(inactiveTriggers);

                    break;
                }
            }
        }
    }

    public static void RemoveChild(
        int index,
        ref GamePhysicsHierarchyDefinition sourceDefinition,
        ref GamePhysicsHierarchyDefinition destinationDefinition,
        ref DynamicBuffer<PhysicsHierarchyInactiveTriggers> sourceInactiveTriggers,
        ref DynamicBuffer<PhysicsHierarchyInactiveTriggers> destinationInactiveTriggers)
    {
        UnityEngine.Assertions.Assert.IsTrue(index >= 0 && index < sourceDefinition.childShapeIndices.Length);

        int shapeShapeIndex = sourceDefinition.childShapeIndices[index];

        UnityEngine.Assertions.Assert.IsTrue(shapeShapeIndex >= 0 && shapeShapeIndex < sourceDefinition.shapes.Length);

        ref var sourceShape = ref sourceDefinition.shapes[shapeShapeIndex];
        int numInactiveTriggers = sourceInactiveTriggers.Length, numShapeIndices = sourceShape.shapeIndices.Length, shapeIndex, i, j;
        for(i = 0; i < numShapeIndices; ++i)
        {
            shapeIndex = sourceShape.shapeIndices[i];
            for(j = 0; j < numInactiveTriggers; ++j)
            {
                if(sourceInactiveTriggers.ElementAt(j).shapeIndex == shapeIndex)
                {
                    sourceInactiveTriggers.RemoveAtSwapBack(j);

                    --numInactiveTriggers;

                    break;
                }
            }
        }

        int numSourceChildShapeNames = sourceShape.childShapeNames.Length,
            numDestinationShapes = destinationDefinition.shapes.Length, 
            numDestinationInactiveShapes = destinationInactiveTriggers.Length;
        for (i = 0; i < numSourceChildShapeNames; ++i)
        {
            ref var childShpaeName = ref sourceShape.childShapeNames[i];
            for (j = 0; j < numDestinationInactiveShapes; ++j)
            {
                shapeIndex = destinationInactiveTriggers[j].shapeIndex;

                UnityEngine.Assertions.Assert.IsTrue(shapeIndex >= 0 && shapeIndex < numDestinationShapes);

                if (destinationDefinition.shapes[shapeIndex].name == childShpaeName)
                {
                    destinationInactiveTriggers.RemoveAtSwapBack(j);

                    --numDestinationInactiveShapes;

                    break;
                }
            }
        }
    }


    public static void RemoveChild(
        int index,
        ref GamePhysicsHierarchyDefinition definition,
        ref DynamicBuffer<PhysicsHierarchyInactiveTriggers> inactiveTriggers)
    {
        UnityEngine.Assertions.Assert.IsTrue(index >= 0 && index < definition.childShapeIndices.Length);

        int shpaeShapeIndex = definition.childShapeIndices[index];

        UnityEngine.Assertions.Assert.IsTrue(shpaeShapeIndex >= 0 && shpaeShapeIndex < definition.shapes.Length);

        ref var sourceShape = ref definition.shapes[shpaeShapeIndex];
        int numInactiveTriggers = inactiveTriggers.Length, numShapeIndices = sourceShape.shapeIndices.Length, shapeIndex, i, j;
        for (i = 0; i < numShapeIndices; ++i)
        {
            shapeIndex = sourceShape.shapeIndices[i];
            for (j = 0; j < numInactiveTriggers; ++j)
            {
                if (inactiveTriggers.ElementAt(j).shapeIndex == shapeIndex)
                {
                    inactiveTriggers.RemoveAtSwapBack(j);

                    --numInactiveTriggers;

                    break;
                }
            }
        }
    }
}

public struct GamePhysicsHierarchyData : IComponentData
{
    public BlobAssetReference<GamePhysicsHierarchyDefinition> definition;
}

public struct GamePhysicsHierarchyBitField : IComponentData
{
    public BitField value;
}

[BurstCompile, 
    CreateAfter(typeof(GameContainerChildSystem)), 
    UpdateInGroup(typeof(InitializationSystemGroup)), 
    UpdateBefore(typeof(PhysicsHierarchyTriggerSystemGroup))/*, UpdateAfter(typeof(BeginFrameEntityCommandSystem))*/]
public partial struct GamePhysicsHierarchyTriggerSystem : ISystem
{
    public struct Change
    {
        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public ComponentLookup<GamePhysicsHierarchyData> instanceMap;

        [ReadOnly]
        public BufferAccessor<GameContainerChild> children;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GamePhysicsHierarchyData> instances;

        public NativeArray<GamePhysicsHierarchyBitField> bitFields;

        public BufferLookup<PhysicsHierarchyInactiveTriggers> inactiveTriggers;

        public void Execute(int index)
        {
            var children = this.children[index];
            BitField destination = default;
            int i, j, numChildren = children.Length;
            for (i = 0; i < numChildren; ++i)
            {
                j = children[i].index;
                if (j < 0)
                    continue;

                destination.Set(j);
            }

            var source = bitFields[index];
            if (source.value == destination)
                return;

            var diff = source.value ^ destination;

            ref var sourceDefinition = ref instances[index].definition.Value;

            bool isAddOrRemove;
            int highestBit = diff.GetHighestBit(sourceDefinition.childShapeIndices.Length);
            Entity childEntity, entity = entityArray[index];
            EntityData<int> childIndex;
            GameContainerChild child;
            SharedMultiHashMap<Entity, EntityData<int>>.Enumerator enumerator;
            DynamicBuffer<PhysicsHierarchyInactiveTriggers> sourceInactiveTriggers = inactiveTriggers[entity], destinationInactiveTriggers;
            for (i = diff.lowerstBit - 1; i < highestBit; ++i)
            {
                if (!diff.Test(i))
                    continue;

                childEntity = Entity.Null;

                isAddOrRemove = destination.Test(i);
                if (isAddOrRemove)
                {
                    for (j = 0; j < numChildren; ++j)
                    {
                        child = children[j];
                        if (child.index == i)
                        {
                            childEntity = child.entity;

                            break;
                        }
                    }
                }
                else
                {
                    enumerator = childIndices.GetValuesForKey(entity);
                    while (enumerator.MoveNext())
                    {
                        childIndex = enumerator.Current;
                        if (childIndex.value == i)
                        {
                            childEntity = childIndex.entity;

                            break;
                        }
                    }
                }

                if (!inactiveTriggers.HasBuffer(childEntity))
                    continue;

                destinationInactiveTriggers = inactiveTriggers[childEntity];

                if (instanceMap.HasComponent(childEntity))
                {
                    ref var destinationDefintion = ref instanceMap[childEntity].definition.Value;

                    if (isAddOrRemove)
                        GamePhysicsHierarchyDefinition.AddChild(
                            i,
                            ref sourceDefinition,
                            ref destinationDefintion,
                            ref sourceInactiveTriggers,
                            ref destinationInactiveTriggers);
                    else
                        GamePhysicsHierarchyDefinition.RemoveChild(
                            i,
                            ref sourceDefinition,
                            ref destinationDefintion,
                            ref sourceInactiveTriggers,
                            ref destinationInactiveTriggers);
                }
                else if (!isAddOrRemove)
                    GamePhysicsHierarchyDefinition.RemoveChild(
                        i,
                        ref sourceDefinition,
                        ref sourceInactiveTriggers);
                /*if (isAddOrRemove)
                GamePhysicsHierarchyDefinition.AddChild(
                    i,
                    ref sourceDefinition,
                    ref defaultDefinition,
                    ref sourceInactiveTriggers,
                    ref destinationInactiveTriggers);
            else
                GamePhysicsHierarchyDefinition.RemoveChild(
                    i,
                    ref sourceDefinition,
                    ref defaultDefinition,
                    ref sourceInactiveTriggers,
                    ref destinationInactiveTriggers);*/
            }

            source.value = destination;
            bitFields[index] = source;
        }
    }

    [BurstCompile]
    public struct ChangeEx : IJobChunk
    {
        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public ComponentLookup<GamePhysicsHierarchyData> instances;

        [ReadOnly]
        public BufferTypeHandle<GameContainerChild> childType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GamePhysicsHierarchyData> instanceType;

        public ComponentTypeHandle<GamePhysicsHierarchyBitField> bitFieldType;

        public BufferLookup<PhysicsHierarchyInactiveTriggers> inactiveShapes;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Change change;
            change.childIndices = childIndices;
            change.instanceMap = instances;
            change.children = chunk.GetBufferAccessor(ref childType);
            change.entityArray = chunk.GetNativeArray(entityType);
            change.instances = chunk.GetNativeArray(ref instanceType);
            change.bitFields = chunk.GetNativeArray(ref bitFieldType);
            change.inactiveTriggers = inactiveShapes;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                change.Execute(i);
        }
    }

    private EntityQuery __group;

    private ComponentLookup<GamePhysicsHierarchyData> __instances;

    private BufferTypeHandle<GameContainerChild> __childType;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GamePhysicsHierarchyData> __instanceType;

    private ComponentTypeHandle<GamePhysicsHierarchyBitField> __bitFieldType;

    private BufferLookup<PhysicsHierarchyInactiveTriggers> __inactiveShapes;

    private SharedMultiHashMap<Entity, EntityData<int>> __childIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GamePhysicsHierarchyData, GameContainerChild>()
                    .WithAllRW<GamePhysicsHierarchyBitField, PhysicsHierarchyInactiveTriggers>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameContainerChild>());

        __instances = state.GetComponentLookup<GamePhysicsHierarchyData>(true);
        __childType = state.GetBufferTypeHandle<GameContainerChild>(true);
        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<GamePhysicsHierarchyData>(true);
        __bitFieldType = state.GetComponentTypeHandle<GamePhysicsHierarchyBitField>();
        __inactiveShapes = state.GetBufferLookup<PhysicsHierarchyInactiveTriggers>();

        __childIndices = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameContainerChildSystem>().childIndices;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ChangeEx change;
        change.childIndices = __childIndices.reader;
        change.instances = __instances.UpdateAsRef(ref state);
        change.childType = __childType.UpdateAsRef(ref state);
        change.entityType = __entityType.UpdateAsRef(ref state);
        change.instanceType = __instanceType.UpdateAsRef(ref state);
        change.bitFieldType = __bitFieldType.UpdateAsRef(ref state);
        change.inactiveShapes = __inactiveShapes.UpdateAsRef(ref state);

        ref var lookupJobManager = ref __childIndices.lookupJobManager;

        var jobHandle = change.ScheduleByRef(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateBefore(typeof(GameNodeInitSystemGroup))]
public partial struct GamePhysicsHierarchyColliderSystem : ISystem
{
    private PhysicsHierarchyColliderSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new PhysicsHierarchyColliderSystemCore(ref state);
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