using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using ZG;

[Serializable, EntityDataStream(serializerType = typeof(EntityComponentStreamSerializer<GameEntityCampOverride>), deserializerType = typeof(GameEntityCampOverrideDeserializer))]
public struct GameEntityCampOverride : IComponentData
{
    public int value;
}

public struct GameEntityCampOverrideDeserializer : IEntityDataStreamDeserializer
{
    public ComponentTypeSet GetComponentTypeSet(in NativeArray<byte> userData)
    {
        return new ComponentTypeSet(userData.IsCreated ? ComponentType.ReadWrite<GameEntityCampOverrideBuffer>() : ComponentType.ReadWrite<GameEntityCampOverride>());
    }

    public void Deserialize(ref UnsafeBlock.Reader reader, ref EntityComponentAssigner assigner, in Entity entity, in NativeArray<byte> userData)
    {
        var value = reader.Read<GameEntityCampOverride>();
        GameEntityCampOverrideBuffer buffer;
        buffer.value = value.value;
        buffer.colliderKey = userData.IsCreated ? userData.Reinterpret<ColliderKey>(1)[0] : ColliderKey.Empty;
        if (assigner.isCreated)
            assigner.SetBuffer(EntityComponentAssigner.BufferOption.AppendUnique,  entity, buffer);
    }
}

public struct GameEntityCampOverrideBuffer : IBufferElementData
{
    public int value;
    public ColliderKey colliderKey;
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true)]
public partial struct GameEntityCampSystem : ISystem
{
    private struct Apply
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;
        [ReadOnly] 
        public NativeArray<GameEntityCampDefault> campDefaults;
        [ReadOnly]
        public BufferLookup<GameEntityCampOverrideBuffer> campOverrideBuffers;
        [ReadOnly]
        public ComponentLookup<GameEntityCampOverride> campOverrides;
        [ReadOnly]
        public ComponentLookup<PhysicsCollider> physicsColliders;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public BufferAccessor<PhysicsTriggerEvent> physicsTriggerEvents;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityCamp> results;
        
        public void Execute(int index)
        {
            var physicsTriggerEvents = this.physicsTriggerEvents[index];
            DynamicBuffer<GameEntityCampOverrideBuffer> campOverrideBuffer;
            Entity effector;
            uint numSubKeyBits, colliderIndexA, colliderIndexB;
            int result = -1;
            foreach(var physicsTriggerEvent in physicsTriggerEvents)
            {
                effector = physicsShapeParents.HasComponent(physicsTriggerEvent.entity) ? physicsShapeParents[physicsTriggerEvent.entity].entity : physicsTriggerEvent.entity;
                if (campOverrides.HasComponent(effector))
                    result = math.max(result, campOverrides[effector].value);
                else if (campOverrideBuffers.HasBuffer(effector))
                {
                    numSubKeyBits = physicsColliders[physicsTriggerEvent.entity].Value.Value.NumColliderKeyBits;
                    campOverrideBuffer = campOverrideBuffers[effector];
                    foreach (var campOverride in campOverrideBuffer)
                    {
                        if(campOverride.colliderKey.Equals(ColliderKey.Empty) || 
                           campOverride.colliderKey.PopSubKey(numSubKeyBits, out colliderIndexA) && 
                           physicsTriggerEvent.colliderKeyB.PopSubKey(numSubKeyBits, out colliderIndexB) && 
                           colliderIndexA == colliderIndexB)
                            result = math.max(result, campOverride.value);
                    }
                }
            }

            if (result == -1)
                result = campDefaults[index].value;

            var camp = camps[index];
            if (camp.value != result)
            {
                camp.value = result;

                results[entityArray[index]] = camp;
            }
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;
        [ReadOnly] 
        public ComponentTypeHandle<GameEntityCampDefault> campDefaultType;
        [ReadOnly]
        public BufferLookup<GameEntityCampOverrideBuffer> campOverrideBuffers;
        [ReadOnly]
        public ComponentLookup<GameEntityCampOverride> campOverrides;
        [ReadOnly]
        public ComponentLookup<PhysicsCollider> physicsColliders;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public BufferTypeHandle<PhysicsTriggerEvent> physicsTriggerEventType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityCamp> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.camps = chunk.GetNativeArray(ref campType);
            apply.campDefaults = chunk.GetNativeArray(ref campDefaultType);
            apply.campOverrideBuffers = campOverrideBuffers;
            apply.campOverrides = campOverrides;
            apply.physicsColliders = physicsColliders;
            apply.physicsShapeParents = physicsShapeParents;
            apply.physicsTriggerEvents = chunk.GetBufferAccessor(ref physicsTriggerEventType);
            apply.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out var i))
                apply.Execute(i);
        }
    }

    private EntityQuery __group;
    
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameEntityCamp> __campType;    
    private ComponentTypeHandle<GameEntityCampDefault> __campDefaultType;
    private BufferLookup<GameEntityCampOverrideBuffer> __campOverrideBuffers;
    private ComponentLookup<GameEntityCampOverride> __campOverrides;
    private ComponentLookup<PhysicsCollider> __physicsColliders;
    private ComponentLookup<PhysicsShapeParent> __physicsShapeParents;
    private BufferTypeHandle<PhysicsTriggerEvent> __physicsTriggerEventType;

    private ComponentLookup<GameEntityCamp> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<PhysicsTriggerEvent>()
                .WithAllRW<GameEntityCamp>()
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __campType  = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __campDefaultType = state.GetComponentTypeHandle<GameEntityCampDefault>(true);
        __campOverrideBuffers = state.GetBufferLookup<GameEntityCampOverrideBuffer>(true);
        __campOverrides = state.GetComponentLookup<GameEntityCampOverride>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __physicsShapeParents = state.GetComponentLookup<PhysicsShapeParent>(true);
        __physicsTriggerEventType  = state.GetBufferTypeHandle<PhysicsTriggerEvent>(true);

        __results = state.GetComponentLookup<GameEntityCamp>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ApplyEx apply;
        apply.entityType = __entityType.UpdateAsRef(ref state);
        apply.campType = __campType.UpdateAsRef(ref state);
        apply.campDefaultType = __campDefaultType.UpdateAsRef(ref state);
        apply.campOverrideBuffers = __campOverrideBuffers.UpdateAsRef(ref state);
        apply.campOverrides = __campOverrides.UpdateAsRef(ref state);
        apply.physicsColliders = __physicsColliders.UpdateAsRef(ref state);
        apply.physicsShapeParents = __physicsShapeParents.UpdateAsRef(ref state);
        apply.physicsTriggerEventType = __physicsTriggerEventType.UpdateAsRef(ref state);
        apply.results = __results.UpdateAsRef(ref state);
        
        state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
    }
}
