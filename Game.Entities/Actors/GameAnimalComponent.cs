using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using ZG;

[assembly: RegisterEntityObject(typeof(GameAnimalComponent))]

public struct GameAnimalFoodsDefinition
{
    public struct Food
    {
        public int itemType;

        public int startRandomGroupIndex;
        public int randomGroupCount;

        public float threshold;
        public float time;
    }

    public struct Item
    {
        public int type;

        public int min;
        public int max;
    }

    public BlobArray<Food> foods;
    public BlobArray<Item> items;
    public BlobArray<RandomGroup> randomGroups;
}

public struct GameAnimalFoodsData : IComponentData
{
    public BlobAssetReference<GameAnimalFoodsDefinition> definition;
}

[Serializable]
public struct GameAnimalData : IComponentData
{
    [Tooltip("最大戒备值，戒备值为零可使用拖动界面选择驯化")]
    public int max;

    public float valueOnNormal;
    public float valueOnKnockedOut;
}

public struct GameAnimalFoodTime : IComponentData
{
    public double value;
}

public struct GameAnimalInfo : IComponentData
{
    public float value;
}

public struct GameAnimalBuff : IComponentData, IBuff<float>
{
    public float value;

    public void Add(float x)
    {
        value += x;
    }

    public void Subtract(float x)
    {
        value -= x;
    }
}

public struct GameAnimalEntityCommandVersion : IComponentData
{
    public int value;
}

[InternalBufferCapacity(8)]
public struct GameAnimalFoodIndex : IBufferElementData
{
    public int value;
}

[EntityComponent]
[EntityComponent(typeof(GameAnimalFoodTime))]
[EntityComponent(typeof(GameAnimalInfo))]
//[EntityComponent(typeof(GameAnimalBuff))]
[EntityComponent(typeof(GameAnimalEntityCommandVersion))]
[EntityComponent(typeof(GameAnimalFoodIndex))]
public class GameAnimalComponent : ComponentDataProxy<GameAnimalData>
{
#if UNITY_EDITOR
    public GameActorDatabase database;

    [CSVField]
    public short 戒备值
    {
        set
        {
            _tamedValue = value;

            GameAnimalData data = base.value;
            data.max = value;
            base.value = data;
        }
    }

    [CSVField]
    public float 戒备增长
    {
        set
        {
            GameAnimalData data = base.value;
            data.valueOnNormal = value;
            base.value = data;
        }
    }

    [CSVField]
    public float 晕眩戒备增长
    {
        set
        {
            GameAnimalData data = base.value;
            data.valueOnKnockedOut = value;
            base.value = data;
        }
    }

    [CSVField]
    public string 食物
    {
        set
        {
            if (string.IsNullOrEmpty(value))
                return;

            string[] names = value.Split('/');
            int numNames = names.Length;

            foodIndices = new int[numNames];
            
            for (int i = 0; i < numNames; ++i)
                _foodIndices[i] = database.GetFoods().IndexOf(names[i]);
        }
    }
#endif
    
    public Action<int> onEat;

    [SerializeField]
    internal int _tamedValue;
    [SerializeField]
    internal float _buff;

    [SerializeField]
    [Index("database.foods", pathLevel = 1, uniqueLevel = 1)]
    internal int[] _foodIndices;

    private bool __isTamed;
    private SharedBuffManager<float, GameAnimalBuff> __buffManager;
    
    public bool isTamed
    {
        get
        {
            return __isTamed;
        }

        set
        {
            if (__isTamed == value)
                return;
            
            if (value)
            {
                this.RemoveComponent<GameAnimalInfo>();
                this.RemoveComponent<GameAnimalBuff>();

                _tamedValue = 0;
            }
            else
            {
                GameAnimalInfo info;
                info.value = _tamedValue;
                this.AddComponentData(info);
                
                GameAnimalBuff buff;
                buff.value = _buff;
                this.AddComponentData(buff);
            }

            __isTamed = value;
        }
    }

    public int tamedValue
    {
        get
        {
            if (gameObjectEntity.isCreated && this.TryGetComponentData<GameAnimalInfo>(out var info))
                _tamedValue = (int)math.round(info.value);

            return _tamedValue;
        }

        set
        {
            if (_tamedValue == value)
                return;
            
            if (!__isTamed)
            {
                GameAnimalInfo info;
                info.value = value;
                this.SetComponentData(info);
            }

            _tamedValue = value;
        }
    }

    public float buff
    {
        get
        {
            return _buff;
        }

        set
        {
            _buff = value;
            
            GameAnimalBuff buff;
            buff.value = value;
            this.SetComponentData(buff);
        }
    }
    
    public SharedBuffManager<float, GameAnimalBuff> bufferManager
    {
        get
        {
            if (!__buffManager.isCreated)
                __buffManager = world == null ? default : world.GetExistingSystemUnmanaged<GameAnimalBuffSystem>().manager;

            return __buffManager;
        }
    }

    public int[] foodIndices
    {
        get
        {
            return _foodIndices;
        }


        set
        {
            _foodIndices = value;

            if(Application.isPlaying && isActiveAndEnabled)
                this.SetBuffer(__GetFoodIndices(value)); ;
        }
    }

    [EntityComponents]
    public Type[] entityComponentTypesEx
    {
        get
        {
            if (value.max > 0)
                return new Type[] { typeof(GameAnimalBuff) };

            return null;
        }
    }
    
    public void SetBuff(float value, float time)
    {
        var bufferManager = this.bufferManager;
        if (!bufferManager.isCreated)
            return;

        EntityData<float> buff;
        buff.entity = entity;
        buff.value = value;
        bufferManager.Set(buff, time);
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        /*if (__isTamed)
        {
            this.RemoveComponent<GameAnimalInfo>();
            this.RemoveComponent<GameAnimalBuff>();
        }
        else*/
        {
            GameAnimalInfo info;
            info.value = _tamedValue;

            assigner.SetComponentData(entity, info);

            if (value.max > 0)
            {
                GameAnimalBuff buff;
                buff.value = _buff;
                assigner.SetComponentData(entity, buff);
            }
            /*else
                assigner.RemoveComponent<GameAnimalBuff>();*/
        }

        assigner.SetBuffer(true, entity, __GetFoodIndices(_foodIndices));
    }

    private GameAnimalFoodIndex[] __GetFoodIndices(int[] values)
    {
        int numValues = values == null ? 0 : values.Length;
        GameAnimalFoodIndex[] foodIndices = new GameAnimalFoodIndex[numValues];
        for (int i = 0; i < numValues; ++i)
            foodIndices[i].value = values[i];

        return foodIndices;
    }
    
    internal void _OnEat(int value)
    {
        if (onEat != null)
            onEat(value);
    }
}