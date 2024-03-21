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

    public int Pick(in GameDeadline time, int version)
    {
        return instance.Do(time, pickTime, pickTime, version);
    }
    
    public int Collect(in GameDeadline time, int version)
    {
        return instance.Do(time, collectTime, collectTime, version);
    }
    
    public int Use(in GameDeadline time, int version)
    {
        return instance.Do(time, useTime, useTime, version);
    }

    public int Drop(in GameDeadline time, int version)
    {
        return instance.Do(time, dropTime, dropTime, version);
    }

    public int Set(in GameDeadline time, int version)
    {
        return instance.Do(time, setTime, setTime, version);
    }

    public int Pick(EntityCommander commander, in GameDeadline time, int version)
    {
        return instance.Do(commander, time, pickTime, pickTime, version);
    }

    public int Collect(EntityCommander commander, in GameDeadline time, int version)
    {
        return instance.Do(commander, time, collectTime, collectTime, version);
    }

    public int Use(EntityCommander commander, in GameDeadline time, int version)
    {
        return instance.Do(commander, time, useTime, useTime, version);
    }

    public int Delete(EntityCommander commander, in GameDeadline time, int version)
    {
        return instance.Do(commander, time, deleteTime, deleteTime, version);
    }

    public int Drop(EntityCommander commander, in GameDeadline time, int version)
    {
        return instance.Do(commander, time, dropTime, dropTime, version);
    }

    public int Set(EntityCommander commander, in GameDeadline time, int version)
    {
        return instance.Do(commander, time, setTime, setTime, version);
    }
}
