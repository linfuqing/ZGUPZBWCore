using ZG;

public class GameWayPointStream : UnityEngine.MonoBehaviour, IEntityDataStreamSerializer
{
    public float maxDistance = 0.5f;

    public GameWayPoint[] values;

    public void Serialize(ref NativeBuffer.Writer writer)
    {
        GameWayData way;
        way.maxDistanceSq = maxDistance * maxDistance;
        writer.SerializeStream(way);
        writer.SerializeStream(values);
    }
}
