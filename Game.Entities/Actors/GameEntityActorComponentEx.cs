using System;
using UnityEngine;
using ZG;

public class GameEntityActorComponentEx : MonoBehaviour
{
    public float pickTime = 1.5f;
    public float collectTime = 3.0f;
    public float useTime = 1.0f;

    public float dropTime = 0.0f;
    public float setTime = 0.0f;

    public GameEntityActorComponent instance { get; private set; }

    protected void Awake()
    {
        instance = GetComponent<GameEntityActorComponent>();
    }

    public int Pick(in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(time, timeEventHandle,  pickTime, pickTime);
    }
    
    public int Collect(in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(time, timeEventHandle, collectTime, collectTime);
    }
    
    public int Use(in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(time, timeEventHandle, useTime, useTime);
    }

    public int Drop(in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(time, timeEventHandle, dropTime, dropTime);
    }

    public int Set(in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(time, timeEventHandle, setTime, setTime);
    }

    public int Pick(EntityCommander commander, in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(commander, time, timeEventHandle, pickTime, pickTime);
    }

    public int Collect(EntityCommander commander, in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(commander, time, timeEventHandle, collectTime, collectTime);
    }

    public int Use(EntityCommander commander, in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(commander, time, timeEventHandle, useTime, useTime);
    }

    public int Drop(EntityCommander commander, in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(commander, time, timeEventHandle, dropTime, dropTime);
    }

    public int Set(EntityCommander commander, in GameDeadline time, in TimeEventHandle timeEventHandle)
    {
        return instance.Do(commander, time, timeEventHandle, setTime, setTime);
    }
}
