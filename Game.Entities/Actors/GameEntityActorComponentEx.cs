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

    public int Pick()
    {
        return instance.Do(pickTime, pickTime);
    }
    
    public int Collect()
    {
        return instance.Do(collectTime, collectTime);
    }
    
    public int Use()
    {
        return instance.Do(useTime, useTime);
    }

    public int Drop()
    {
        return instance.Do(dropTime, dropTime);
    }

    public int Set()
    {
        return instance.Do(setTime, setTime);
    }

    public int Pick(EntityCommander commander)
    {
        return instance.Do(commander, pickTime, pickTime);
    }

    public int Collect(EntityCommander commander)
    {
        return instance.Do(commander, collectTime, collectTime);
    }

    public int Use(EntityCommander commander)
    {
        return instance.Do(commander, useTime, useTime);
    }

    public int Delete(EntityCommander commander)
    {
        return instance.Do(commander, deleteTime, deleteTime);
    }

    public int Drop(EntityCommander commander)
    {
        return instance.Do(commander, dropTime, dropTime);
    }

    public int Set(EntityCommander commander)
    {
        return instance.Do(commander, setTime, setTime);
    }
}
