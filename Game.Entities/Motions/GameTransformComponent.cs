using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Math = ZG.Mathematics.Math;

/*[assembly: RegisterGenericComponentType(typeof(GameTransformSource<GameTransform>))]
[assembly: RegisterGenericComponentType(typeof(GameTransformDestination<GameTransform>))]*/
[assembly: RegisterGenericComponentType(typeof(GameTransformKeyframe<GameTransform>))]
[assembly: RegisterGenericComponentType(typeof(GameTransformVelocity<GameTransform, GameTransformVelocity>))]

public interface IGameTransformVelocity<T> where T : IGameTransform<T>
{
    T SmoothDamp(in T source, in T destination, float smoothTime, float deltaTime);
}

public interface IGameTransform<T> where T : IGameTransform<T>
{
    T LerpTo(in T value, float scale);
}

[Serializable]
public struct GameTransformKeyframe<T> : IBufferElementData where T : unmanaged, IGameTransform<T>
{
    public struct Comparer : System.Collections.Generic.IComparer<GameDeadline>
    {
        public int Compare(GameDeadline x, GameDeadline y)
        {
            return x.CompareTo(y);
        }
    }

    public struct ReadOnlyListWrapper : IReadOnlyListWrapper<GameDeadline, DynamicBuffer<GameTransformKeyframe<T>>>
    {
        public int GetCount(DynamicBuffer<GameTransformKeyframe<T>> list) => list.Length;

        public GameDeadline Get(DynamicBuffer<GameTransformKeyframe<T>> list, int index) => list[index].time;
    }

    public GameDeadline time;
    public T value;

    public T LerpTo(in GameTransformKeyframe<T> transform, double time)
    {
        double deltaTime = transform.time - this.time;
        float scale = math.abs(deltaTime) > math.DBL_MIN_NORMAL ? (float)((time - this.time) / deltaTime) : 0.5f;
        scale = math.clamp(scale, 0.0f, 1.0f);

        return value.LerpTo(transform.value, scale);
    }

    public static int BinarySearch(ref DynamicBuffer<GameTransformKeyframe<T>> keyframes, in GameDeadline time)
    {
        return keyframes.BinarySearch(time, new Comparer(), new ReadOnlyListWrapper());
    }

    public static void Insert(
        ref DynamicBuffer<GameTransformKeyframe<T>> keyframes, 
        in GameTransformKeyframe<T> keyframe)
    {
        int keyframeIndex = BinarySearch(ref keyframes, keyframe.time);
        if (keyframeIndex >= 0 && keyframeIndex < keyframes.Length && keyframes[keyframeIndex].time == keyframe.time)
            keyframes[keyframeIndex] = keyframe;
        else
            keyframes.Insert(keyframeIndex + 1, keyframe);
    }
}

/*[Serializable]
public struct GameTransformSource<T> : IComponentData where T : struct, IGameTransform<T>
{
    public GameTransform<T> value;
    public GameTransform<T> oldValue;
}

[Serializable]
public struct GameTransformDestination<T> : IComponentData where T : struct, IGameTransform<T>
{
    public GameTransform<T> value;
}*/

//[Serializable]
public struct GameTransformVelocity<TTransform, TVelocity> : IComponentData 
    where TTransform : struct, IGameTransform<TTransform>
    where TVelocity : struct, IGameTransformVelocity<TTransform>
{
    public TVelocity value;
}

//[Serializable]
public struct GameTransform : IGameTransform<GameTransform>
{
    public RigidTransform value;

    public GameTransform LerpTo(in GameTransform value, float scale)
    {
        GameTransform result;
        result.value = Math.Lerp(this.value, value.value, scale);
        return result;
    }
}

//[Serializable]
public struct GameTransformVelocity : IGameTransformVelocity<GameTransform>
{
    public float angular;
    public float3 linear;

    public GameTransform SmoothDamp(in GameTransform source, in GameTransform destination, float smoothTime, float deltaTime)
    {
        GameTransform transform;
        transform.value.pos = Math.SmoothDamp(source.value.pos, destination.value.pos, ref linear, smoothTime, float.MaxValue, deltaTime);

        float delta = Math.Angle(source.value.rot, destination.value.rot);
        if (delta > math.FLT_MIN_NORMAL)
        {
            delta = Math.SmoothDampAngle(delta, 0.0f, ref angular, smoothTime, float.MaxValue, deltaTime) / delta;
            delta = 1.0f - math.clamp(delta, 0.0f, 1.0f);

            transform.value.rot = math.slerp(
                source.value.rot,
                destination.value.rot,
                delta);
        }
        else
        {
            angular = 0.0f;

            transform.value.rot = source.value.rot;
        }

        return transform;
    }
}

[WriteGroup(typeof(LocalToWorld))]
public struct GameTransformData : IComponentData
{

}

[EntityComponent(typeof(UnityEngine.Transform))]
[EntityComponent(typeof(GameTransformData))]
[EntityComponent(typeof(LocalToWorld))]
[EntityComponent(typeof(GameTransformVelocity<GameTransform, GameTransformVelocity>))]
public class GameTransformComponent : EntityProxyComponent
{
    private bool __isActive = true;

    public bool isActive
    {
        get
        {
            return __isActive;
        }

        set
        {
            if (__isActive == value)
                return;

            if (value)
                this.AddComponent<GameTransformVelocity<GameTransform, GameTransformVelocity>>();
            else
                this.RemoveComponent<GameTransformVelocity<GameTransform, GameTransformVelocity>>();

            __isActive = value;
        }
    }
}
