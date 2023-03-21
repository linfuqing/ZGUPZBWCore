using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameEntityActionSystemGroup))]
public partial struct GameAnimatorHitSystem : ISystem
{
    private struct Hit
    {
        [ReadOnly]
        public BufferAccessor<GameEntitySharedHit> hits;
        public BufferAccessor<GameAnimatorHitInfo> infos;

        public void Execute(int index)
        {
            var hits = this.hits[index];
            int numHits = hits.Length;
            if (numHits < 1)
                return;

            var infos = this.infos[index];
            GameAnimatorHitInfo info;
            GameEntitySharedHit hit;
            int i, j, numInfos = infos.Length;
            for (i = 0; i < numHits; ++i)
            {
                hit = hits[i];

                for (j = 0; j < numInfos; ++j)
                {
                    info = infos[j];
                    if (info.version == hit.version && info.actionIndex == hit.actionIndex && info.entity == hit.entity)
                    {
                        infos.RemoveAt(j--);

                        --numInfos;

                        break;
                    }
                }
            }
            for (i = 0; i < numHits; ++i)
            {
                hit = hits[i];

                info.version = hit.version;
                info.actionIndex = hit.actionIndex;
                info.time = hit.time;
                info.entity = hit.entity;

                infos.Add(info);
            }
        }
    }

    [BurstCompile]
    private struct HitEx : IJobChunk
    {
        [ReadOnly]
        public BufferTypeHandle<GameEntitySharedHit> hitType;
        public BufferTypeHandle<GameAnimatorHitInfo> infoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Hit hit;
            hit.hits = chunk.GetBufferAccessor(ref hitType);
            hit.infos = chunk.GetBufferAccessor(ref infoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                hit.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(ComponentType.ReadOnly<GameEntitySharedHit>(), ComponentType.ReadWrite<GameAnimatorHitInfo>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        HitEx hit;
        hit.hitType = state.GetBufferTypeHandle<GameEntitySharedHit>(true);
        hit.infoType = state.GetBufferTypeHandle<GameAnimatorHitInfo>();

        state.Dependency = hit.ScheduleParallel(__group, state.Dependency);
    }
}