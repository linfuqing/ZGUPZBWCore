using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;
using ZG;

/*#region GameFactory_0
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameFormulaFactoryStatus>.Deserializer, ComponentDataDeserializationSystem<GameFormulaFactoryStatus>.DeserializerFactory>))]
[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryStatus), 0)]
#endregion*/

#region GameFormulaFactoryStatus_3
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataFactoryDeserializationSystem_3.Deserializer, GameDataFactoryDeserializationSystem_3.DeserializerFactory>))]
[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactoryDeserializationSystem_3), 3)]
#endregion


[Serializable]
public struct GameFormulaFactoryStatus_3
{
    public GameFormulaFactoryStatus.Status value;

    public int formulaIndex;

    public int level;

    public GameFormulaFactoryStatus Convert()
    {
        GameFormulaFactoryStatus result;
        result.value = value;
        result.formulaIndex = formulaIndex;
        result.level = level;
        result.entity = Entity.Null;
        return result;
    }
}

[DisableAutoCreation, UpdateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem))]
public partial class GameDataFactoryDeserializationSystem_3 : EntityDataDeserializationComponentSystem<
        GameFormulaFactoryStatus,
        GameDataFactoryDeserializationSystem_3.Deserializer,
        GameDataFactoryDeserializationSystem_3.DeserializerFactory>
{
    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<int> indices;

        public NativeArray<GameFormulaFactoryStatus> instances;

        public GameFormulaFactoryStatusWrapper wrapper;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            var instance = reader.Read<GameFormulaFactoryStatus_3>().Convert();

            if (wrapper.TryGet(instance, out int temp))
                wrapper.Set(ref instance, indices[temp]);
            else
                wrapper.Invail(ref instance);

            instances[index] = instance;
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public NativeArray<int> indices;

        public ComponentTypeHandle<GameFormulaFactoryStatus> instanceType;

        public GameFormulaFactoryStatusWrapper wrapper;

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Deserializer deserializer;
            deserializer.indices = indices;
            deserializer.instances = chunk.GetNativeArray(ref instanceType);
            deserializer.wrapper = wrapper;

            return deserializer;
        }
    }

    private EntityDataIndexContainerDeserializationSystem __containerSystem;

    //public override bool isSingle => true;

    protected override void OnCreate()
    {
        base.OnCreate();

        __containerSystem = World.GetOrCreateSystemManaged<GameFormulaFactoryStatusContainerDeserializationSystem>();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        __containerSystem.AddReadOnlyDependency(Dependency);
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle);

        DeserializerFactory deserializerFactory;
        deserializerFactory.indices = __containerSystem.indices;
        deserializerFactory.instanceType = GetComponentTypeHandle<GameFormulaFactoryStatus>();
        deserializerFactory.wrapper = default;

        return deserializerFactory;
    }

}