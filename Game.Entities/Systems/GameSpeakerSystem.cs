using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Physics;
using Unity.Physics.Systems;
using ZG;

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)),
    //UpdateBefore(typeof(EndFramePhysicsSystem)),
    UpdateAfter(typeof(GameEntityActionSystemGroup))]
public partial struct GameSpeakerSystem : ISystem
{
    public struct Collector : ICollector<DistanceHit>
    {
        private GameActionTargetType __type;
        private double __time;
        private Entity __entity;
        private GameEntityNode __node;

        private NativeSlice<RigidBody> __rigidBodies;
        private ComponentLookup<GameEntityCamp> __camps;
        private ComponentLookup<GameSpeakerInfo> __speakerInfos;

        public bool EarlyOutOnFirstHit => false;

        public int NumHits => 0;

        public float MaxFraction { get; private set; }

        public GameEntityNode result { get; private set; }

        public Collector(
            GameActionTargetType type,
            float maxFraction,
            double time,
            Entity entity,
            GameEntityNode node,
            NativeSlice<RigidBody> rigidBodies,
            ComponentLookup<GameEntityCamp> camps,
            ComponentLookup<GameSpeakerInfo> speakerInfos)
        {
            __type = type;

            __time = time;

            __entity = entity;

            __node = node;

            __rigidBodies = rigidBodies;

            __camps = camps;
            __speakerInfos = speakerInfos;

            MaxFraction = maxFraction;
            result = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            RigidBody rigidBody = __rigidBodies[hit.RigidBodyIndex];

            if (!__camps.HasComponent(rigidBody.Entity))
                return false;

            GameEntityNode node;
            node.camp = __camps[rigidBody.Entity].value;
            node.entity = rigidBody.Entity;
            if (!__node.Predicate(__type, node))
                return false;

            if (!__speakerInfos.HasComponent(rigidBody.Entity))
                return false;

            GameSpeakerInfo speakerInfo;
            speakerInfo.time = __time;
            speakerInfo.target = __entity;

            __speakerInfos[rigidBody.Entity] = speakerInfo;

            return true;
        }
    }
    
    private struct Speak
    {
        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameSpeakerData> instances;

        [ReadOnly]
        public NativeArray<GameEntityHealthDamageCount> healthDamageCounts;

        [ReadOnly]
        public BufferAccessor<GameEntityHealthDamage> healthDamages;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameSpeakerInfo> speakerInfos;

        public void Execute(int index)
        {
            var instance = instances[index];
            if (instance.layerMask == 0)
                return;

            var healthDamages = this.healthDamages[index];
            int numHealthDamages = healthDamages.Length;
            if (numHealthDamages <= healthDamageCounts[index].value)
                return;

            var healthDamage = healthDamages[numHealthDamages - 1];
            if (!camps.HasComponent(healthDamage.entity))
                return;

            int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entityArray[index]);
            if (rigidbodyIndex == -1)
            {
                UnityEngine.Debug.LogError("Speaker's Rigidbody Invail!");

                return;
            }

            var rigidbodies = collisionWorld.Bodies;
            var rigidbody = rigidbodies[rigidbodyIndex];
            if (!camps.HasComponent(rigidbody.Entity))
                return;

            int camp = camps[rigidbody.Entity].value;
            if (camp == camps[healthDamage.entity].value)
                return;
            
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = instance.radius;
            pointDistanceInput.Position = rigidbody.WorldFromBody.pos;
            pointDistanceInput.Filter = rigidbody.Collider.Value.Filter;
            pointDistanceInput.Filter.CollidesWith = (uint)(int)instance.layerMask;

            GameEntityNode node;
            node.camp = camps[rigidbody.Entity].value;
            node.entity = rigidbody.Entity;
            
            var collector = new Collector(
                instance.type,
                instance.radius,
                healthDamage.time,
                healthDamage.entity,
                node,
                rigidbodies,
                camps,
                speakerInfos);
            collisionWorld.CalculateDistance(pointDistanceInput, ref collector);
        }
    }

    [BurstCompile]
    private struct SpeakEx : IJobChunk
    {
        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameSpeakerData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthDamageCount> healthDamageCountType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityHealthDamage> healthDamageType;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameSpeakerInfo> speakerInfos;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Speak speak;
            speak.collisionWorld = collisionWorld;
            speak.entityArray = chunk.GetNativeArray(entityType);
            speak.instances = chunk.GetNativeArray(ref instanceType);
            speak.healthDamageCounts = chunk.GetNativeArray(ref healthDamageCountType);
            speak.healthDamages = chunk.GetBufferAccessor(ref healthDamageType);
            speak.camps = camps;
            speak.speakerInfos = speakerInfos;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                speak.Execute(i);
        }
    }

    private EntityQuery __group;
    private SharedPhysicsWorld __physicsWorld;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntityHealthDamage>(),
            ComponentType.ReadOnly<GameSpeakerData>(),
            ComponentType.Exclude<Disabled>());

        __physicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        SpeakEx speak;
        speak.collisionWorld = __physicsWorld.collisionWorld;
        speak.entityType = state.GetEntityTypeHandle();
        speak.healthDamageCountType = state.GetComponentTypeHandle<GameEntityHealthDamageCount>(true);
        speak.healthDamageType = state.GetBufferTypeHandle<GameEntityHealthDamage>(true);
        speak.instanceType = state.GetComponentTypeHandle<GameSpeakerData>(true);
        speak.camps = state.GetComponentLookup<GameEntityCamp>(true);
        speak.speakerInfos = state.GetComponentLookup<GameSpeakerInfo>();

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = speak.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
