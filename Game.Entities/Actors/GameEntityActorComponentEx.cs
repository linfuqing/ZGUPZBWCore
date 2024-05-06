using System;
using UnityEngine;
using ZG;

public class GameEntityActorComponentEx : MonoBehaviour
{
    public float pickTime = 1.5f;
    public float collectTime = 3.0f;
    public float useTime = 1.0f;

    public float deleteTime = 0.0f;
    public float dropTime = 0.0f;
    public float setTime = 0.0f;

    public GameEntityActorComponent instance { get; private set; }

    protected void Awake()
    {
        instance = GetComponent<GameEntityActorComponent>();
    }

    public int Pick(in GameDeadline time)
    {
        return instance.Do(time, pickTime, pickTime);
    }
    
    public int Collect(in GameDeadline time)
    {
        return instance.Do(time, collectTime, collectTime);
    }
    
    public int Use(in GameDeadline time)
    {
        return instance.Do(time, useTime, useTime);
    }

    public int Drop(in GameDeadline time)
    {
        return instance.Do(time, dropTime, dropTime);
    }

    public int Set(in GameDeadline time)
    {
        return instance.Do(time, setTime, setTime);
    }

    public int Pick(EntityCommander commander, in GameDeadline time)
    {
        return instance.Do(commander, time, pickTime, pickTime);
    }

    public int Collect(EntityCommander commander, in GameDeadline time)
    {
        return instance.Do(commander, time, collectTime, collectTime);
    }

    public int Use(EntityCommander commander, in GameDeadline time)
    {
        return instance.Do(commander, time, useTime, useTime);
    }

    public int Delete(EntityCommander commander, in GameDeadline time)
    {
        return instance.Do(commander, time, deleteTime, deleteTime);
    }

    public int Drop(EntityCommander commander, in GameDeadline time)
    {
        return instance.Do(commander, time, dropTime, dropTime);
    }

    public int Set(EntityCommander commander, in GameDeadline time)
    {
        return instance.Do(commander, time, setTime, setTime);
    }
}
