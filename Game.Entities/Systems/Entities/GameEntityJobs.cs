using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Extensions;
using ZG;
using Math = ZG.Mathematics.Math;

public interface IGameEntityActionHandler
{
    bool Create(
        int index,
        double time,
        in float3 targetPosition, 
        in Entity entity,
        in RigidTransform transform,
        in GameActionData instance);

    bool Init(
        int index,
        float elapsedTime,
        double time,
        in Entity entity,
        in RigidTransform transform,
        in GameActionData instance);

    void Hit(
        int index,
        float elapsedTime,
        double time,
        in Entity entity,
        in Entity target,
        in RigidTransform transform,
        in GameActionData instance);

    void Damage(
        int index,
        int count,
        float elapsedTime,
        double time,
        in Entity entity,
        in Entity target,
        in float3 position,
        in float3 normal,
        in GameActionData instance);

    void Destroy(
        int index,
        float elapsedTime,
        double time,
        in Entity entity,
        in RigidTransform transform,
        in GameActionData instance);
}

public interface IGameEntityActionFactory<T> where T : struct, IGameEntityActionHandler
{
    T Create(in ArchetypeChunk chunk);
}

public struct GameEntityTransform
{
    public float elapsedTime;
    public RigidTransform value;

    public GameEntityTransform LerpTo(in GameEntityTransform transform, float fraction)
    {
        GameEntityTransform result;
        result.elapsedTime = math.lerp(elapsedTime, transform.elapsedTime, fraction);
        result.value = Math.Lerp(value, transform.value, fraction);

        return result;
    }
}


public static class GameEntityUtility
{
    public static int Hit(
        this ref DynamicBuffer<GameActionEntity> actionEntities,
        in Entity entity,
        float3 normal, 
        float elaspedTime,
        float interval,
        float value)
    {
        int i, numActionEntities = actionEntities.Length;
        GameActionEntity actionEntity = default;
        for (i = 0; i < numActionEntities; ++i)
        {
            actionEntity = actionEntities[i];
            if (actionEntity.target == entity)
                break;
        }

        if (i < numActionEntities)
        {
            if (interval > math.FLT_MIN_NORMAL && actionEntity.elaspedTime < elaspedTime)
            {
                int count = 0;
                float nextTime = actionEntity.elaspedTime + interval;
                while (nextTime <= elaspedTime)
                {
                    ++count;

                    actionEntity.elaspedTime = nextTime;

                    nextTime += interval;
                }

                if (count > 0)
                {
                    actionEntity.delta = value * count;
                    actionEntity.hit += actionEntity.delta;
                    actionEntity.normal += normal * count;
                    actionEntities[i] = actionEntity;

                    return count;
                }
            }

            return 0;
        }

        actionEntity.hit = value;
        actionEntity.delta = value;
        actionEntity.elaspedTime = elaspedTime;
        actionEntity.normal = normal;
        actionEntity.target = entity;

        actionEntities.Add(actionEntity);

        return 1;
    }

}