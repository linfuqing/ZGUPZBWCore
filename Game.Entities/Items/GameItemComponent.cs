using System;
using Unity.Entities;
using UnityEngine;
using ZG;

public struct GameItem
{
    public int parentHandle;
    public byte parentChildIndex;
    public byte count;
    public short itemIndex;
    public short durability;
    public float time;
    //public int[] childHandles;
}

/*[Serializable]
public struct GameItemInfo
{
    public int handle;
    public byte childIndex;
}

[Serializable]
public struct GameItemData
{
    public byte count;
    public short itemIndex;
    public float durability;
    public float time;

    public int siblingHandle;
    public GameItemInfo parent;
}*/

public struct GameItemTimeScale : IComponentData
{
    public float value;
}

public struct GameItemRoot : IComponentData, IEnableableComponent
{
    public GameItemHandle handle;
}

public struct GameItemSibling : IBufferElementData, IEnableableComponent
{
    public GameItemHandle handle;
}

public struct GameItemDontDestroyOnDead  :IComponentData
{

}

/*[Serializable]
public struct GameItemRoot : IComponentData
{
    public int handle;
}*/

[DisallowMultipleComponent]
[EntityComponent(typeof(GameItemRoot))]
[EntityComponent(typeof(GameItemTimeScale))]
public class GameItemComponent : EntityProxyComponent, IEntityComponent
{
    [SerializeField]
    internal float _timeScale = 1.0f;

    public GameItemHandle handle
    {
        get
        {
            return this.GetComponentData<GameItemRoot>().handle;
        }

        set
        {
            GameItemRoot root;
            root.handle = value;
            this.SetComponentData(root);
            this.SetComponentEnabled<GameItemRoot>(true);
        }
    }

    public float timeScale
    {
        get
        {
            return _timeScale;
        }

        set
        {
            if (Mathf.Approximately(_timeScale, value))
                return;

            if (gameObjectEntity.isCreated)
            {
                var timeScale = this.GetComponentData<GameItemTimeScale>();
                timeScale.value += value - _timeScale;
                this.SetComponentData(timeScale);
            }

            _timeScale = value;
        }
    }

    public float overrideTimeScale => this.GetComponentData<GameItemTimeScale>().value;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameItemTimeScale timeScale;
        timeScale.value = _timeScale;
        assigner.SetComponentData(entity, timeScale);
    }
}
