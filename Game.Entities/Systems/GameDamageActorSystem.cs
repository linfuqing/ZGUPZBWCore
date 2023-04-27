using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameRandomSpawnerSystem)), UpdateAfter(typeof(GameEntityHealthSystem))]
public partial struct GameDamageActorSystem : ISystem
{
    private struct Act
    {
        [ReadOnly]
        public BufferAccessor<GameDamageActorLevel> levels;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameEntityHealthDamage> damages;
        
        public NativeArray<GameDamageActorHit> hits;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomActorNode> actors;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomSpawnerNode> spawners;

        public void Execute(int index)
        {
            var damage = damages[index];
            if(math.abs(damage.value) > math.FLT_MIN_NORMAL)
            {
                var hit = hits[index];

                var levels = this.levels[index];
                GameDamageActorLevel level;
                Entity entity = entityArray[index];
                float value;
                int length = levels.Length;
                for(int i = 0; i < length; ++i)
                {
                    level = levels[i];
                    value = (level.flag & GameDamageActorFlag.Loop) == GameDamageActorFlag.Loop ? hit.value % level.hit : hit.value;
                    if (value >= level.hit || value + damage.value < level.hit)
                        continue;

                    if ((level.flag & GameDamageActorFlag.Action) == GameDamageActorFlag.Action)
                    {
                        if (actors.HasBuffer(entity))
                            actors[entity].Reinterpret<int>().Add(level.sliceIndex);

                        continue;
                    }

                    if (spawners.HasBuffer(entity))
                    {
                        spawners[entity].Reinterpret<int>().Add(level.sliceIndex);
                        spawners.SetBufferEnabled(entity, true);
                    }
                }

                hit.value += damage.value;
                hits[index] = hit;
            }
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferTypeHandle<GameDamageActorLevel> levelType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthDamage> damageType;

        public ComponentTypeHandle<GameDamageActorHit> hitType;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomActorNode> actors;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomSpawnerNode> spawners;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.levels = chunk.GetBufferAccessor(ref levelType);
            act.entityArray = chunk.GetNativeArray(entityType);
            act.damages = chunk.GetNativeArray(ref damageType);
            act.hits = chunk.GetNativeArray(ref hitType);
            act.actors = actors;
            act.spawners = spawners;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                act.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntityHealthDamage>(),
            ComponentType.ReadOnly<GameDamageActorLevel>(),
            ComponentType.ReadWrite<GameDamageActorHit>());

        __group.SetChangedVersionFilter(typeof(GameEntityHealthDamage));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ActEx act;
        act.entityType = state.GetEntityTypeHandle();
        act.damageType = state.GetComponentTypeHandle<GameEntityHealthDamage>(true);
        act.levelType = state.GetBufferTypeHandle<GameDamageActorLevel>(true);
        act.hitType = state.GetComponentTypeHandle<GameDamageActorHit>();
        act.actors = state.GetBufferLookup<GameRandomActorNode>();
        act.spawners = state.GetBufferLookup<GameRandomSpawnerNode>();

        state.Dependency = act.ScheduleParallel(__group, state.Dependency);
    }
}
