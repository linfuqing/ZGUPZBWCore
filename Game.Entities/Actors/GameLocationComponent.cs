using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using ZG;

[EntityComponent(typeof(GameLocationData))]
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

    private List<CallbackHandle<GameLocationCallbackData>> __callbackHandles;

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
        var locations = new GameLocationData[numLocations];

        Action<GameLocationCallbackData> enter, exit;
        for (int i = 0; i < numLocations; ++i)
        {
            ref var destination = ref locations[i];
            var source = _locations[i];

            enter = x => source.onEnter.Invoke();
            exit = x => source.onExit.Invoke();

            destination.id = StringManager.Intern(source.name).value;
            destination.radiusSq = source.radius * source.radius;
            destination.position = source.position;
            destination.enter = enter.Register();
            destination.exit = exit.Register();

            if (__callbackHandles == null)
                __callbackHandles = new List<CallbackHandle<GameLocationCallbackData>>();

            __callbackHandles.Add(destination.enter);
            __callbackHandles.Add(destination.exit);
        }

        assigner.SetBuffer(true, entity, locations);
    }
}
