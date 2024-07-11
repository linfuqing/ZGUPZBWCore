using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using ZG;

[BurstCompile, 
 CreateAfter(typeof(GamePhysicsWorldBuildSystem)), 
 UpdateInGroup(typeof(GameUpdateSystemGroup)),
    //UpdateBefore(typeof(EndFramePhysicsSystem)),
    UpdateAfter(typeof(GameEntityActionSystemGroup))]
public partial struct GameSpeakerSystem : ISystem
{
    public struct Collector : ICollector<DistanceHit>
    {
        //private GameActionTargetType __type;
        //private int __sourceCamp;
        private int __sourceCamp;
        private double __time;
        private Entity __source;
        private Entity __destination;
        //private GameEntityNode __node;

        private NativeSlice<RigidBody> __rigidBodies;
        private ComponentLookup<GameEntityCamp> __camps;
        private ComponentLookup<GameWatcherData> __watchers;
        private ComponentLookup<GameWatcherInfo> __watcherInfos;
        private ComponentLookup<GameSpeakerInfo> __speakerInfos;

        public bool EarlyOutOnFirstHit => false;

        public int NumHits => 0;

        public float MaxFraction { get; private set; }

        public GameEntityNode result { get; private set; }

        public Collector(
            //GameActionTargetType type,
            //int sourceCamp, 
            int sourceCamp, 
            float maxFraction,
            double time,
            in Entity source,
            in Entity destination,
            //in GameEntityNode node,
            in NativeSlice<RigidBody> rigidBodies,
            in ComponentLookup<GameEntityCamp> camps,
            in ComponentLookup<GameWatcherData> watchers, 
            ref ComponentLookup<GameWatcherInfo> watcherInfos, 
            ref ComponentLookup<GameSpeakerInfo> speakerInfos)
        {
            //__type = type;
            //__sourceCamp = sourceCamp;
            __sourceCamp = sourceCamp;

            __time = time;

            __source = source;
            __destination = destination;

            //__node = node;

            __rigidBodies = rigidBodies;

            __camps = camps;
            __watchers = watchers;
            __watcherInfos = watcherInfos;
            __speakerInfos = speakerInfos;

            MaxFraction = maxFraction;
            result = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            var rigidBody = __rigidBodies[hit.RigidBodyIndex];

            if (!__camps.HasComponent(rigidBody.Entity))
                return false;

            int camp = __camps[rigidBody.Entity].value;
            if (camp == __sourceCamp)
            {
                if (!__watcherInfos.HasComponent(rigidBody.Entity))
                    return false;

                var watcher = __watchers[rigidBody.Entity];

                GameWatcherInfo watcherInfo;
                watcherInfo.type = GameWatcherInfo.Type.Main;
                watcherInfo.time = __time + new Random(RandomUtility.Hash(__time) ^ (uint)rigidBody.Entity.Index).NextFloat(watcher.minTime, watcher.maxTime);
                watcherInfo.target = __destination;

                __watcherInfos[rigidBody.Entity] = watcherInfo;
            }
            else// if (camp == __camp)
            {
                if (!__speakerInfos.HasComponent(rigidBody.Entity))
                    return false;

                GameSpeakerInfo speakerInfo;
                speakerInfo.time = __time;
                speakerInfo.target = __source;

                __speakerInfos[rigidBody.Entity] = speakerInfo;
            }

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

        [ReadOnly]
        public ComponentLookup<GameWatcherData> watchers;
        
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameWatcherInfo> watcherInfos;

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

            int camp = camps[rigidbody.Entity].value, sourceCamp = camps[healthDamage.entity].value;
            if (camp == sourceCamp)
                return;
            
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = instance.radius;
            pointDistanceInput.Position = rigidbody.WorldFromBody.pos;
            pointDistanceInput.Filter = rigidbody.Collider.Value.Filter;
            pointDistanceInput.Filter.CollidesWith = (uint)(int)instance.layerMask;

            var collector = new Collector(
                //instance.type,
                //camp,
                sourceCamp, 
                instance.radius,
                healthDamage.time, 
                healthDamage.entity,
                rigidbody.Entity, 
                rigidbodies,
                camps,
                watchers, 
                ref watcherInfos, 
                ref speakerInfos);
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

        [ReadOnly]
        public ComponentLookup<GameWatcherData> watchers;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameWatcherInfo> watcherInfos;

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
            speak.watchers = watchers;
            speak.watcherInfos = watcherInfos;
            speak.speakerInfos = speakerInfos;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                speak.Execute(i);
        }
    }

    private EntityQuery __group;
    
    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameSpeakerData> __instanceType;

    private ComponentTypeHandle<GameEntityHealthDamageCount> __healthDamageCountType;

    private BufferTypeHandle<GameEntityHealthDamage> __healthDamageType;

    private ComponentLookup<GameEntityCamp> __camps;

    private ComponentLookup<GameWatcherData> __watchers;

    private ComponentLookup<GameWatcherInfo> __watcherInfos;

    private ComponentLookup<GameSpeakerInfo> __speakerInfos;

    private SharedPhysicsWorld __physicsWorld;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder.WithAll<GameEntityHealthDamage, GameSpeakerData>()
                .Build(ref state);
        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameEntityHealthDamage>());

        __entityType = state.GetEntityTypeHandle();
        __healthDamageCountType = state.GetComponentTypeHandle<GameEntityHealthDamageCount>(true);
        __healthDamageType = state.GetBufferTypeHandle<GameEntityHealthDamage>(true);
        __instanceType = state.GetComponentTypeHandle<GameSpeakerData>(true);
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __watchers = state.GetComponentLookup<GameWatcherData>(true);
        __watcherInfos = state.GetComponentLookup<GameWatcherInfo>();
        __speakerInfos = state.GetComponentLookup<GameSpeakerInfo>();
        
        __physicsWorld = state.WorldUnmanaged.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        SpeakEx speak;
        speak.collisionWorld = __physicsWorld.collisionWorld;
        speak.entityType = __entityType.UpdateAsRef(ref state);
        speak.healthDamageCountType = __healthDamageCountType.UpdateAsRef(ref state);
        speak.healthDamageType = __healthDamageType.UpdateAsRef(ref state);
        speak.instanceType = __instanceType.UpdateAsRef(ref state);
        speak.camps = __camps.UpdateAsRef(ref state);
        speak.watchers = __watchers.UpdateAsRef(ref state);
        speak.watcherInfos = __watcherInfos.UpdateAsRef(ref state);
        speak.speakerInfos = __speakerInfos.UpdateAsRef(ref state);

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = speak.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
