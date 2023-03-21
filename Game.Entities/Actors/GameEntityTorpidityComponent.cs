using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

[Serializable]
public struct GameEntityTorpidity : IComponentData
{
    public float value;
}

[Serializable]
public struct GameEntityTorpiditySpeedScale : IComponentData
{
    public half value;
}

[Serializable, InternalBufferCapacity(1)]
public struct GameEntityTorpidityBuff : IBufferElementData
{
    public float value;
    public float duration;
}

[Serializable]
public struct GameEntityTorpidityData : IComponentData
{
    [Tooltip("最小理智，晕眩状态下回复量大于该值恢复正常状态")]
    public int min;

    [Tooltip("最大理智上限")]
    public int max;

    [Tooltip("普通状态晕眩恢复")]
    public float buffOnNormal;

    [Tooltip("晕倒状态晕眩恢复")]
    [UnityEngine.Serialization.FormerlySerializedAs("buffScaleOnKnockedOut")]
    public float buffOnKnockedOut;
}

[EntityComponent(typeof(GameEntityTorpidity))]
[EntityComponent(typeof(GameEntityTorpiditySpeedScale))]
[EntityComponent(typeof(GameEntityTorpidityBuff))]
public class GameEntityTorpidityComponent : ZG.ComponentDataProxy<GameEntityTorpidityData>
{
#if UNITY_EDITOR
    [CSVField]
    public int 理智最小值
    {
        set
        {
            GameEntityTorpidityData data = base.value;
            data.min = value;
            base.value = data;
        }
    }

    [CSVField]
    public int 理智最大值
    {
        set
        {
            GameEntityTorpidityData data = base.value;
            data.max = value;
            base.value = data;

            _torpidity = value;
        }
    }

    [CSVField]
    public float 正常状态理智恢复速度
    {
        set
        {
            GameEntityTorpidityData data = base.value;
            data.buffOnKnockedOut = value;
            base.value = data;

            _buff = value;
        }
    }

    [CSVField]
    public float 晕眩状态理智恢复速度
    {
        set
        {
            GameEntityTorpidityData data = base.value;
            data.buffOnKnockedOut = value;
            base.value = data;
        }
    }
#endif
    
    [SerializeField]
    internal int _torpidity;

    [SerializeField]
    [Tooltip("基本晕眩恢复")]
    internal float _buff;

    public int torpidity
    {
        get
        {
            if (gameObjectEntity.isAssigned)
            {
                GameEntityTorpidity torpidity = this.GetComponentData<GameEntityTorpidity>();
                _torpidity = (int)math.round(torpidity.value);
            }

            return _torpidity;
        }
        
        set
        {
            /*if (_torpidity == value)
                return;*/

            if (gameObjectEntity.isAssigned)
            {
                GameEntityTorpidity torpidity = this.GetComponentData<GameEntityTorpidity>();

                torpidity.value = value;

                this.SetComponentData(torpidity);
            }

            _torpidity = value;
        }
    }

    /*public float buff
    {
        get
        {
            return _buff;
        }

        set
        {
            var buffs = this.GetBuffer<GameEntityTorpidityBuff>();
            var buff = buffs[0];
            buff.value = value;
            buffs[0] = buff;
            this.SetBuffer(buffs);

            _buff = value;
        }
    }*/
    
    public void SetBuff(float value, float duration)
    {
        UnityEngine.Assertions.Assert.IsTrue(duration > math.FLT_MIN_NORMAL);
        
        GameEntityTorpidityBuff buff;
        buff.value = value;
        buff.duration = duration;

        this.AppendBuffer(buff);
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        _value.buffOnNormal = _buff;

        base.Init(entity, assigner);

        GameEntityTorpidity torpidity;
        torpidity.value = _torpidity;
        assigner.SetComponentData(entity, torpidity);

        GameEntityTorpiditySpeedScale speedScale;
        speedScale.value = (half)1.0f;
        assigner.SetComponentData(entity, speedScale);

        /*GameEntityTorpidityBuff buff;
        buff.duration = 0.0f;
        buff.value = _buff;

        assigner.SetBuffer(true, entity, new GameEntityTorpidityBuff[] { buff });*/
    }
}
