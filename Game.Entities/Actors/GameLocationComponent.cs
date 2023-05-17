using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using ZG;

[EntityComponent(typeof(GameLocation))]
public class GameLocationComponent : MonoBehaviour, IEntitySystemStateComponent
{
    [Serializable]
    public struct Location
    {
        public string name;

        public float radius;
        public float3 position;

        public UnityEvent onEnter;
        public UnityEvent onExit;
    }

    [SerializeField]
    internal Location[] _locations;

    private List<CallbackHandle<Entity>> __callbackHandles;

    void OnDestroy()
    {
        if(__callbackHandles != null)
        {
            foreach (var callbackHandle in __callbackHandles)
                callbackHandle.Unregister();
        }
    }

    void IEntitySystemStateComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        int numLocations = _locations.Length;
        var locations = new GameLocation[numLocations];

        Action<Entity> enter, exit;
        for (int i = 0; i < numLocations; ++i)
        {
            ref var destination = ref locations[i];
            var source = _locations[i];

            enter = x => source.onEnter.Invoke();
            exit = x => source.onExit.Invoke();

            destination.radiusSq = source.radius * source.radius;
            destination.position = source.position;
            destination.enter = enter.Register();
            destination.exit = exit.Register();

            if (__callbackHandles == null)
                __callbackHandles = new List<CallbackHandle<Entity>>();

            __callbackHandles.Add(destination.enter);
            __callbackHandles.Add(destination.exit);
        }
    }
}
