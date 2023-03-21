using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;
using ZG;

public interface IGameFootstepResources
{
    int particleSystemCount { get; }

    ParticleSystem LoadParticleSystem(int index);
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GameFootstepParticleSystem : SystemBase
{
    [BurstCompile]
    private struct Apply : IJobParticleSystem
    {
        public int particleOffset;
        public int tagOffset;
        [ReadOnly]
        public NativeArray<GameFootstepTag> tags;

        public void Execute(ParticleSystemJobData jobData, int startIndex, int count)
        {
            int particleStartIndex = math.max(particleOffset, startIndex),
                particleCount = count - (particleStartIndex - startIndex),
                tagStartIndex = tagOffset + (particleStartIndex - particleOffset), 
                particleIndex;
            GameFootstepTag tag;
            ParticleSystemNativeArray3 positions = jobData.positions, 
                rotations = jobData.rotations, 
                sizes = jobData.sizes;
            NativeArray<float> px = positions.x, 
                py = positions.y, 
                pz = positions.z, 
                rx = rotations.x, 
                ry = rotations.y, 
                rz = rotations.z, 
                sx = sizes.x, 
                sy = sizes.y, 
                sz = sizes.z;
            for (int i = 0; i < particleCount; ++i)
            {
                tag = tags[i + tagStartIndex];

                particleIndex = i + particleStartIndex;

                if (px.IsCreated)
                    px[particleIndex] += tag.position.x;

                if (px.IsCreated)
                    py[particleIndex] += tag.position.y;

                if (px.IsCreated)
                    pz[particleIndex] += tag.position.z;

                if (rx.IsCreated)
                {
                    if (ry.IsCreated && rz.IsCreated)
                        rotations[particleIndex] = (tag.rotation * Quaternion.Euler(rotations[particleIndex])).eulerAngles;
                    else
                        rx[particleIndex] += math.degrees(ZG.Mathematics.Math.GetEulerZ(tag.rotation));
                }

                if (sx.IsCreated)
                    sx[particleIndex] *= tag.scale;

                if (sy.IsCreated)
                    sy[particleIndex] *= tag.scale;

                if (sz.IsCreated)
                    sz[particleIndex] *= tag.scale;
            }
        }

        public void Execute(ParticleSystemJobData jobData, int index) => Execute(jobData, index, 1);

        public void Execute(ParticleSystemJobData jobData) => Execute(jobData, 0, jobData.count);
    }

    private struct ParticleManager : IGameFootstepTagManager
    {
        //public static readonly int InnerLoopBatchCount = 4;

        public IGameFootstepResources resources;

        public bool ScheduleParallel(in NativeArray<GameFootstepTag> tags, int offset, int count, int index, ref JobHandle jobHandle)
        {
            var particleSystem = resources.LoadParticleSystem(index);
            if (particleSystem == null)
                return false;

            Apply apply;
            apply.particleOffset = particleSystem.particleCount;

            particleSystem.Emit(count);
            //particleSystem.Play();

            apply.tagOffset = offset;
            apply.tags = tags;

            jobHandle = apply.Schedule(particleSystem, /*InnerLoopBatchCount, */jobHandle);

            return true;
        }
    }

    private GameFootstepManager __manager;

    protected override void OnCreate()
    {
        base.OnCreate();

        __manager = World.GetOrCreateSystemUnmanaged<GameFootstepSystem>().manager;
    }

    protected override void OnUpdate()
    {
        ref var state = ref this.GetState();

        var resources = GameFootstepSettings.resources;
        __manager.Reset(resources == null ? 0 : resources.particleSystemCount);

        if (__manager.isVail)
        {
            ParticleManager manager;
            manager.resources = resources;

            if (__manager.Apply(manager, out var jobHandle))
                state.Dependency = jobHandle;

            /*var tagCounts = __manager.GetTagCounts();

            int numTagCounts = tagCounts.Length;

            int count = 0;
            for (int i = 0; i < numTagCounts; ++i)
                count += tagCounts[i];

            if (count > 0)
            {
                ParticleSystem particleSystem;
                var particles = new NativeArray<ParticleSystem.Particle>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int offset = 0, particleOffset;
                for (int i = 0; i < numTagCounts; ++i)
                {
                    count = tagCounts[i];
                    if (count > 0)
                    {
                        particleSystem = resources.LoadParticleSystem(i);
                        if (particleSystem != null)
                        {
                            particleOffset = particleSystem.particleCount;

                            particleSystem.Emit(count);

                            particleSystem.GetParticles(particles.GetSubArray(offset, count), count, particleOffset);
                        }

                        offset += count;
                    }
                }

                var tags = __manager.GetTagResults();
                GameFootstepTag tag;
                ParticleSystem.Particle particle;
                count = tags.Length;
                Job.WithCode(() =>
                {
                    for (int i = 0; i < count; ++i)
                    {
                        tag = tags[i];

                        particle = particles[i];

                        particle.position += (Vector3)tag.position;
                        particle.rotation3D = (tag.rotation * Quaternion.Euler(particle.rotation3D)).normalized.eulerAngles;

                        particles[i] = particle;
                    }
                }).Run();

                offset = 0;
                for (int i = 0; i < numTagCounts; ++i)
                {
                    count = tagCounts[i];
                    if (count > 0)
                    {
                        particleSystem = resources.LoadParticleSystem(i);
                        if (particleSystem != null)
                            particleSystem.SetParticles(particles.GetSubArray(offset, count), count, particleSystem.particleCount - count);

                        offset += count;

                        tagCounts[i] = 0;
                    }
                }

                particles.Dispose();
            }*/
        }
    }
}