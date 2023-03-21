using System;
using Unity.Entities;
using Unity.Mathematics;
using ZG;
using Math = ZG.Mathematics.Math;

[Serializable]
[EntityDataStream(serializerType = typeof(EntityBufferStreamSerializer<GameWayPoint>), deserializerType = typeof(EntityBufferDeserializer<GameWayPoint>))]
public struct GameWayPoint : IBufferElementData
{
    public float suction;
    public float backwardFraction;
    public float forwardFraction;
    public float3 value;

    public static bool FindClosestPoint(
        DynamicBuffer<GameWayPoint> points, 
        float3 position, 
        ref float maxDistanceSq, 
        out float3 closestPoint, 
        out float fraction, 
        out int index)
    {
        index = -1;

        fraction = 1.0f;

        closestPoint = position;

        int length = points.Length;
        if (length < 2)
            return false;
        
        bool result = false;
        float distance, currentFraction;
        float3 start = points[0].value, end, point, normal, vector;
        for(int i  = 1; i < length; ++i)
        {
            end = points[i].value;
            normal = end - start;
            vector = Math.ProjectSafe(position - start, normal);
            if (math.dot(vector, normal) < 0.0f)
            {
                currentFraction = 0.0f;
                vector = float3.zero;
            }
            else
            {
                currentFraction = math.lengthsq(vector);
                distance = math.lengthsq(normal);
                if (currentFraction > distance)
                {
                    currentFraction = 1.0f;
                    vector = normal;
                }
                else
                    currentFraction = math.sqrt(currentFraction) * math.rsqrt(distance);
            }

            point = vector + start;

            distance = math.distancesq(point, position);
            if(maxDistanceSq > math.FLT_MIN_NORMAL ? maxDistanceSq > distance : true)
            {
                maxDistanceSq = distance;

                closestPoint = point;

                fraction = currentFraction;

                index = i - 1;

                result = true;
            }

            start = end;
        }

        return result;
    }

    public static float3 Move(
        int sign, 
        in float3 point, 
        in DynamicBuffer<GameWayPoint> points, 
        ref int pointIndex, 
        ref float fraction, 
        ref GameWayPoint start, 
        ref GameWayPoint end)
    {
        if (sign < 0)
        {
            if (fraction > end.backwardFraction)
                return math.lerp(start.value, end.value, fraction);// return point;
            
            if (pointIndex > 0)
            {
                --pointIndex;

                fraction = 1.0f;

                end = start;

                start = points[pointIndex];
                    
                return Move(sign, end.value, points, ref pointIndex, ref fraction, ref start, ref end);
            }

            fraction = 0.0f;

            return start.value;
        }
        else if (sign == 0 || fraction < start.forwardFraction)
            //return point;
            return math.lerp(start.value, end.value, fraction);

        if (pointIndex < points.Length - 2)
        {
            ++pointIndex;

            fraction = 0.0f;

            start = end;

            end = points[pointIndex + 1];

            return Move(sign, start.value, points, ref pointIndex, ref fraction, ref start, ref end);
        }

        fraction = 1.0f;

        return end.value;
    }
}

[Serializable]
public struct GameWayData : IComponentData
{
    public float maxDistanceSq;
}

[EntityComponent(typeof(GameWayPoint))]
public class GameWayComponent : ComponentDataProxy<GameWayData>
{
    public GameWayPoint[] points;

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        assigner.SetBuffer(true, entity, points);
    }
}
