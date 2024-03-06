using Unity.Collections;
using Unity.Entities;
using ZG;

public struct GameRangeSpawnerStatus : IComponentData
{
    
}

/*public partial struct GameRangeSpawnerSystem : ISystem
{
    private struct Spawn
    {
        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;
        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;
        [ReadOnly]
        public BufferAccessor<PhysicsTriggerEvent> physicsTriggerEvents;
        public void Execute(int index)
        {
            var physicsTriggerEvents = this.physicsTriggerEvents[index];
        }
    }
}*/
