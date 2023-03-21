using System;
using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using ZG;

[EntityComponent(typeof(GameSpawnedInstanceData))]
public class GameSpawnedInstanceComponent : MonoBehaviour, IEntityComponent
{
#if UNITY_EDITOR
    [CSVField]
    public string ½ÚµãÃû³Æ
    {
        set
        {
            _assetIndex = database.GetAssets().IndexOf(value);

            UnityEngine.Assertions.Assert.AreNotEqual(-1, _assetIndex, value);
        }
    }

    public GameActorDatabase database;
#endif

    [SerializeField, Index("database.assets", pathLevel = -1)]
    internal int _assetIndex = -1;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        UnityEngine.Assertions.Assert.AreNotEqual(-1, _assetIndex, name);

        GameSpawnedInstanceData instance;
        instance.assetIndex = _assetIndex;
        assigner.SetComponentData(entity, instance);
    }

    /*void OnValidate()
    {
        if (database == null)
            return;

        int index = -1;
        foreach(var asset in database.GetAssets())
        {
            ++index;

            if (name.Contains(PinYinConverter.Get(asset.GetName()).ToLower()))
            {
                _assetIndex = index;

                return;
            }
        }

        Debug.LogError("Fail To Get Asset", this);
    }*/
}
