using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

public enum GameActionSpawnType
{
    Init,
    Hit,
    Damage
}

[Flags]
public enum GameActionSpawnFlag
{
    HitToPicked = 0x01
}

public struct GameItemSpawnStatus : IComponentData
{
    public int nodeStatus;
    public float entityHealth;
    public float entityTorpidity;
    public float animalValue;
    public float chance;
    public GameItemHandle handle;
}

[Serializable]
public struct GameActionSpawn
{
    public GameActionSpawnType type;

    [Index("assets", pathLevel = -1)]
    public int assetIndex;

    public float chance;
}

[Serializable]
public struct GameBuff : IComponentData
{
    [Tooltip("对目标每秒持续加血")]
    public float healthPerTime;
    [Tooltip("对目标持续加血时间")]
    public float healthTime;
    [Tooltip("对目标持续晕眩时间")]
    public float torpidityTime;

    public static GameBuff operator +(GameBuff x, GameBuff y)
    {
        x.healthPerTime += y.healthPerTime;
        x.healthTime += y.healthTime;
        x.torpidityTime += y.torpidityTime;

        return x;
    }
}

[Serializable, InternalBufferCapacity(9)]
public struct GameEntityDefence : IBufferElementData
{
    public float value;

    public static GameEntityDefence zero
    {
        get
        {
            GameEntityDefence defence;
            defence.value = 0;
            return defence;
        }
    }

    public static implicit operator float(GameEntityDefence x)
    {
        return x.value;
    }

    public static implicit operator GameEntityDefence(float x)
    {
        GameEntityDefence defence;
        defence.value = x;
        return defence;
    }
}

[Serializable, InternalBufferCapacity(9)]
public struct GameActionAttack : IBufferElementData
{
    public float value;

    public static implicit operator float(GameActionAttack x)
    {
        return x.value;
    }

    public static implicit operator GameActionAttack(float x)
    {
        GameActionAttack attack;
        attack.value = x;
        return attack;
    }
}

[Serializable]
public struct GameActionBuff : IComponentData
{
    public GameBuff value;
}

[EntityComponent(typeof(GameEntityDefence))]
public class GameEntityDataComponent : GameEntityComponentEx, IEntityComponent
{
#if UNITY_EDITOR
    public const int DEFENCE_COUNT = 9;

    [CSVField]
    public int 理防
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[1] = value;
        }
    }

    [CSVField]
    public int 法防
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[2] = value;
        }
    }

    [CSVField]
    public int 耐寒
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[3] = value;
        }
    }

    [CSVField]
    public float 耐热
    {
        set
        {
            if (defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[4] = value;
        }
    }

    [CSVField]
    public float 刺抗
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[5] = value;
        }
    }

    [CSVField]
    public float 钝抗
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[6] = value;
        }
    }

    [CSVField]
    public float 耐毒
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[7] = value;
        }
    }

    [CSVField]
    public float 耐震
    {
        set
        {
            if (_defences == null)
                _defences = new float[DEFENCE_COUNT];
            else if (_defences.Length < DEFENCE_COUNT)
                Array.Resize(ref _defences, DEFENCE_COUNT);

            _defences[8] = value;
        }
    }
#endif

    [SerializeField]
    internal float[] _defences;

    public override IReadOnlyCollection<TypeIndex> actionEntityArchetype
    {
        get
        {
            return new List<TypeIndex>(GameEntityActorComponent.ActionComponentTypes)
            {
                TypeManager.GetTypeIndex<GameActionBuff>(),
                TypeManager.GetTypeIndex<GameActionAttack>()
            };
        }
    }

    public float[] defences
    {
        get
        {
            return _defences;
        }

        set
        {
            _defences = value;

#if UNITY_EDITOR
            if(Application.isPlaying)
#endif
                this.SetBuffer(__GetDefences(value));
        }
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, __GetDefences(_defences));
    }

    private GameEntityDefence[] __GetDefences(float[] values)
    {
        int length = values.Length;
        GameEntityDefence[] defences = new GameEntityDefence[values.Length];
        for (int i = 0; i < length; ++i)
            defences[i] = values[i];

        return defences;
    }
}
