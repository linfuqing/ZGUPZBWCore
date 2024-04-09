using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameRandomSpawnerSystem)), UpdateBefore(typeof(GameEntityHealthSystem))]
public partial struct GameDamageActorSystem : ISystem
{
    private struct Act
    {
        [ReadOnly]
        public BufferAccessor<GameDamageActorLevel> levels;

        [ReadOnly]
        public BufferAccessor<GameEntityHealthDamage> damages;
        
        [ReadOnly]
        public NativeArray<GameEntityHealthDamageCount> damageCounts;

        public NativeArray<GameDamageActorHit> hits;

        public BufferAccessor<GameRandomActorNode> actors;

        public BufferAccessor<GameRandomSpawnerNode> spawners;

        public GameStatusActorFlag Execute(int index)
        {
            GameStatusActorFlag flag = 0;
            var damageCount = damageCounts[index];
            var damages = this.damages[index];
            int numDamages = damages.Length;
            if(numDamages > damageCount.value)
            {
                float damageValue = 0.0f;
                for (int i = damageCount.value; i < numDamages; ++i)
                    damageValue += damages[i].value;
                
                var hit = hits[index];

                var spawners = index < this.spawners.Length ? this.spawners[index] : default;
                var actors = index < this.actors.Length ? this.actors[index] : default;
                var levels = this.levels[index];
                GameDamageActorLevel level;
                GameRandomActorNode actor;
                GameRandomSpawnerNode spawner;
                float value;
                int length = levels.Length;
                for(int i = 0; i < length; ++i)
                {
                    level = levels[i];
                    value = (level.flag & GameDamageActorFlag.Loop) == GameDamageActorFlag.Loop ? hit.value % level.hit : hit.value;
                    if (value >= level.hit || value + damageValue < level.hit)
                        continue;

                    if ((level.flag & GameDamageActorFlag.Action) == GameDamageActorFlag.Action)
                    {
                        if (actors.IsCreated)
                        {
                            flag |= GameStatusActorFlag.Action;

                            actor.sliceIndex = level.sliceIndex;
                            actors.Add(actor);
                        }

                        continue;
                    }

                    if (spawners.IsCreated)
                    {
                        flag |= GameStatusActorFlag.Normal;

                        spawner.sliceIndex = level.sliceIndex;
                        spawners.Add(spawner);
                    }
                }

                hit.value += damageValue;
                hits[index] = hit;
            }

            return flag;
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk
    {
        [ReadOnly]
        public BufferTypeHandle<GameDamageActorLevel> levelType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityHealthDamage> damageType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthDamageCount> damageCountType;

        public ComponentTypeHandle<GameDamageActorHit> hitType;

        public BufferTypeHandle<GameRandomActorNode> actorType;

        public BufferTypeHandle<GameRandomSpawnerNode> spawnerType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.levels = chunk.GetBufferAccessor(ref levelType);
            act.damages = chunk.GetBufferAccessor(ref damageType);
            act.damageCounts = chunk.GetNativeArray(ref damageCountType);
            act.hits = chunk.GetNativeArray(ref hitType);
            act.actors = chunk.GetBufferAccessor(ref actorType);
            act.spawners = chunk.GetBufferAccessor(ref spawnerType);

            GameStatusActorFlag flag;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                flag = act.Execute(i);

                if ((flag & GameStatusActorFlag.Normal) == GameStatusActorFlag.Normal)
                    chunk.SetComponentEnabled(ref spawnerType, i, true);

                if ((flag & GameStatusActorFlag.Action) == GameStatusActorFlag.Action)
                    chunk.SetComponentEnabled(ref actorType, i, true);
            }
        }
    }

    private EntityQuery __group;

    private BufferTypeHandle<GameDamageActorLevel> __levelType;

    private BufferTypeHandle<GameEntityHealthDamage> __damageType;

    private ComponentTypeHandle<GameEntityHealthDamageCount> __damageCountType;

    private ComponentTypeHandle<GameDamageActorHit> __hitType;

    private BufferTypeHandle<GameRandomActorNode> __actorType;

    private BufferTypeHandle<GameRandomSpawnerNode> __spawnerType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameEntityHealthDamage, GameDamageActorLevel>()
                .WithAllRW<GameDamageActorHit>()
                .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameEntityHealthDamage>());

        __levelType = state.GetBufferTypeHandle<GameDamageActorLevel>(true);
        __damageType = state.GetBufferTypeHandle<GameEntityHealthDamage>(true);
        __damageCountType = state.GetComponentTypeHandle<GameEntityHealthDamageCount>(true);
        __hitType = state.GetComponentTypeHandle<GameDamageActorHit>();
        __actorType = state.GetBufferTypeHandle<GameRandomActorNode>();
        __spawnerType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ActEx act;
        act.levelType = __levelType.UpdateAsRef(ref state);
        act.damageType = __damageType.UpdateAsRef(ref state);
        act.damageCountType = __damageCountType.UpdateAsRef(ref state);
        act.hitType = __hitType.UpdateAsRef(ref state);
        act.actorType = __actorType.UpdateAsRef(ref state);
        act.spawnerType = __spawnerType.UpdateAsRef(ref state);

        state.Dependency = act.ScheduleParallelByRef(__group, state.Dependency);
    }
}
