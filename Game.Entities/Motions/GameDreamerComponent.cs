using System;
using System.Collections.Generic;
using Unity.Entities;
using ZG;

[assembly: RegisterEntityObject(typeof(GameDreamerComponent))]

public enum GameDreamerStatus
{
    Normal = 0x00,
    Sleep = 0x05,
    Dream = 0x09, 
    Awake = 0x0D,

    Unknown = 0x08
}

[InternalBufferCapacity(4)]
public struct GameDream : IBufferElementData
{
    public int nextIndex;

    public float sleepTime;
    public float awakeTime;
}

public struct GameDreamerEvent : IBufferElementData
{
    public GameDreamerStatus status;
    public int version;
    public int index;
    public GameDeadline time;
}

public struct GameDreamerVersion : IComponentData
{
    public int value;
}

public struct GameDreamer : IComponentData
{
    public GameDreamerStatus status;
    public GameDeadline time;

    public override string ToString()
    {
        return $"GameDreamer({status}, {time})";
    }
}

public struct GameDreamerInfo : IComponentData
{
    public int currentIndex;
    public int nextIndex;
    public int level;

    public override string ToString()
    {
        return $"GameDreamerInfo({currentIndex}, {nextIndex}, {level})";
    }
}

[EntityComponent]
[EntityComponent(typeof(GameNodeDelay))]
[EntityComponent(typeof(GameDream))]
[EntityComponent(typeof(GameDreamerInfo))]
[EntityComponent(typeof(GameDreamerVersion))]
[EntityComponent(typeof(GameDreamerEvent))]
public class GameDreamerComponent : EntityProxyComponent, IEntityComponent
{
    [Serializable]
    public struct Dream
    {
        public float sleepTime;
        public float awakeTime;
    }

    [Serializable]
    internal struct Dreams
    {
        public Dream[] dreams;
    }
    
    public event Action onSleep;
    public event Action onAwake;
    public event Action onSleeping;
    public event Action onAwaking;

    [UnityEngine.SerializeField]
    internal Dreams[] _dreams = null;

    //private GameSyncSystemGroup __syncSystemGroup;

    private int __version = 0;
    private int __offset = -1;

    public GameDreamerStatus status
    {
        get;
        private set;
    }
    
    public int index
    {
        get;
        private set;
    }

    public int level
    {
        get;
        private set;
    }

    public GameDeadline time
    {
        get;
        private set;
    }

    public Dream this[int index, int level]
    {
        get
        {
            return _dreams[index].dreams[level];
        }
    }
    
    public GameDreamerComponent()
    {
        index = -1;

        level = -1;
    }

    public int GetDreamsIndex(int dreamIndex)
    {
        int numDreams = _dreams == null ? 0 : _dreams.Length, length, i;
        Dreams dreams;
        for (i = 0; i < numDreams; ++i)
        {
            dreams = _dreams[i];
            length  = dreams.dreams == null ? 0 : dreams.dreams.Length;
            if (dreamIndex < length)
                break;

            dreamIndex -= length;
        }

        return i < numDreams ? i : -1;
    }

    public bool TryGet(out GameDreamer dreamer)
    {
        if (!gameObjectEntity.isCreated)
        {
            dreamer = default;

            return false;
        }

        return this.TryGetComponentData(out dreamer);
    }

    public void Set(in GameDreamer dreamer, in GameDreamerInfo dreamerInfo)
    {
        this.AddComponentData(dreamer);

        this.SetComponentData(dreamerInfo);
    }

    public void Set(EntityCommander commander, in GameDreamer dreamer, in GameDreamerInfo dreamerInfo)
    {
        Entity entity = base.entity;

        commander.AddComponentData(entity, dreamer);

        commander.SetComponentData(entity, dreamerInfo);
    }

    public void Reset(EntityCommander commander)
    {
        commander.RemoveComponent<GameDreamer>(entity);
    }

    public void Reset()
    {
        this.RemoveComponent<GameDreamer>();
    }

    public void SleepRightNow(in GameDeadline time, int index, int level = 0)
    {
        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Sleep;
        dreamer.time = time;

        GameDreamerInfo dreamerInfo;
        dreamerInfo.level = level;
        dreamerInfo.currentIndex = -1;
        dreamerInfo.nextIndex = level;

        Dreams dreams;
        for (int i = 0; i < index; ++i)
        {
            dreams = _dreams[i];
            dreamerInfo.nextIndex += dreams.dreams == null ? 0 : dreams.dreams.Length;
        }

        if (gameObjectEntity.isCreated)
        {
            this.AddComponentData(dreamer);

            this.SetComponentData(dreamerInfo);

            return;
        }

        status = GameDreamerStatus.Sleep;
        this.index = index;
        this.level = level;

        __offset = dreamerInfo.nextIndex;

        this.time = dreamer.time;
    }

    public void SleepRightNow(EntityCommander commander, in GameDeadline time, int index, int level = 0)
    {
        Entity entity = base.entity;

        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Sleep;
        dreamer.time = time;

        commander.AddComponentData(entity, dreamer);

        GameDreamerInfo dreamerInfo;
        dreamerInfo.level = level;
        dreamerInfo.currentIndex = -1;
        dreamerInfo.nextIndex = level;

        Dreams dreams;
        for (int i = 0; i < index; ++i)
        {
            dreams = _dreams[i];
            dreamerInfo.nextIndex += dreams.dreams == null ? 0 : dreams.dreams.Length;
        }

        commander.SetComponentData(entity, dreamerInfo);
    }

    public void DreamRightNow(EntityCommander commander, in GameDeadline time)
    {
        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Sleep;
        dreamer.time = time;// __syncSystemGroup.now;
        commander.SetComponentData(entity, dreamer);
    }

    public void DreamRightNow(in GameDeadline time)
    {
        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Sleep;
        dreamer.time = time;// __syncSystemGroup.now;
        this.SetComponentData(dreamer);
    }

    public void DreamRightNow(in GameDeadline time, int index, int level)
    {
        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Dream;
        dreamer.time = time;

        this.AddComponentData(dreamer);

        GameDreamerInfo dreamerInfo;
        dreamerInfo.level = level;
        dreamerInfo.currentIndex = level;

        Dreams dreams;
        for (int i = 0; i < index; ++i)
        {
            dreams = _dreams[i];
            dreamerInfo.currentIndex += dreams.dreams == null ? 0 : dreams.dreams.Length;
        }

        dreamerInfo.nextIndex = dreamerInfo.currentIndex < _dreams[index].dreams.Length - 1 ? dreamerInfo.currentIndex + 1 : -1;

        this.SetComponentData(dreamerInfo);
    }

    public void DreamRightNow(EntityCommander commander, in GameDeadline time, int index, int level)
    {
        Entity entity = base.entity;

        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Dream;
        dreamer.time = time;

        commander.AddComponentData(entity, dreamer);

        GameDreamerInfo dreamerInfo;
        dreamerInfo.level = level;
        dreamerInfo.currentIndex = level;

        Dreams dreams;
        for (int i = 0; i < index; ++i)
        {
            dreams = _dreams[i];
            dreamerInfo.currentIndex += dreams.dreams == null ? 0 : dreams.dreams.Length;
        }

        dreamerInfo.nextIndex = dreamerInfo.currentIndex < _dreams[index].dreams.Length - 1 ? dreamerInfo.currentIndex + 1 : -1;

        commander.SetComponentData(entity, dreamerInfo);
    }

    public void AwakeRightNow(EntityCommander commander, in GameDeadline time)
    {
        //UnityEngine.Debug.Log($"Awake {transform.root.name} : {time.count} : {time} : {world.Name}");

        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Awake;
        dreamer.time = time;
        commander.SetComponentData(entity, dreamer);
    }

    public void AwakeRightNow(in GameDeadline time)
    {
        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Awake;
        dreamer.time = time;
        this.SetComponentData(dreamer);
    }

    public void AwakeRightNow(in GameDeadline time, int index, int level)
    {
        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Awake;
        dreamer.time = time;

        GameDreamerInfo dreamerInfo;
        dreamerInfo.level = level;
        dreamerInfo.currentIndex = level;
        
        Dreams dreams;
        for (int i = 0; i < index; ++i)
        {
            dreams = _dreams[i];
            dreamerInfo.currentIndex += dreams.dreams == null ? 0 : dreams.dreams.Length;
        }

        dreamerInfo.nextIndex = -1;

        if (gameObjectEntity.isCreated)
        {
            this.AddComponentData(dreamer);

            this.SetComponentData(dreamerInfo);

            return;
        }

        status = GameDreamerStatus.Awake;
        this.index = index;
        this.level = level;

        __offset = dreamerInfo.nextIndex;

        this.time = dreamer.time;
    }

    public void AwakeRightNow(EntityCommander commander, in GameDeadline time, int index, int level)
    {
        //UnityEngine.Debug.Log($"Awake {transform.root.name} : {time.count} : {time} : {index} : {level} : {world.Name}");

        Entity entity = base.entity;

        GameDreamer dreamer;
        dreamer.status = GameDreamerStatus.Awake;
        dreamer.time = time;

        commander.AddComponentData(entity, dreamer);

        GameDreamerInfo dreamerInfo;
        dreamerInfo.level = level;
        dreamerInfo.currentIndex = level;

        Dreams dreams;
        for (int i = 0; i < index; ++i)
        {
            dreams = _dreams[i];
            dreamerInfo.currentIndex += dreams.dreams == null ? 0 : dreams.dreams.Length;
        }

        dreamerInfo.nextIndex = -1;

        commander.SetComponentData(entity, dreamerInfo);
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        /*GameDreamerEvent temp;
        temp.status = GameDreamerStatus.Unknown;
        temp.version = 0;
        temp.index = -1;
        temp.time = default;
        this.SetComponentData(temp);*/

        GameDreamerVersion version;
        version.value = __version;
        assigner.SetComponentData(entity, version);

        int length = _dreams == null ? 0 : _dreams.Length;
        if (length > 0)
        {
            var dreamResults = new List<GameDream>();

            int index = 0, count, i;
            Dream dream;
            GameDream result;
            foreach (Dreams dreams in _dreams)
            {
                count = dreams.dreams == null ? 0 : dreams.dreams.Length;
                for (i = 0; i < count; ++i)
                {
                    dream = dreams.dreams[i];

                    result.nextIndex = i < count - 1 ? index + 1 : -1;
                    result.sleepTime = dream.sleepTime;
                    result.awakeTime = dream.awakeTime;

                    dreamResults.Add(result);

                    ++index;
                }
            }

            assigner.SetBuffer(true, entity, dreamResults.ToArray());
        }

        /*if (status != GameDreamerStatus.Normal)
        {
            GameDreamer dreamer;

            dreamer.status = status;

            if (status == GameDreamerStatus.Sleep)
            {
                dreamer.currentIndex = -1;
                dreamer.nextIndex = __offset;
            }
            else
            {
                dreamer.currentIndex = __offset;
                dreamer.nextIndex = dreamer.currentIndex >= 0 && dreamer.currentIndex < index ? __dreams[dreamer.currentIndex].nextIndex : -1;
            }

            dreamer.level = level;
            dreamer.time = time;

            this.AddComponentData(dreamer);
        }*/
    }

    protected void OnDestroy()
    {
        if (status != GameDreamerStatus.Normal)
        {
            status = GameDreamerStatus.Unknown;

            if (onAwaking != null)
                onAwaking();
        }

        index = -1;

        level = -1;

        __offset = -1;
    }
    
    internal void _Changed(GameDreamerEvent result)
    {
        if (__version >= result.version)
            return;

        __version = result.version;

        __offset = result.index;

        time = result.time;

        Dreams dreams;
        int length = _dreams == null ? 0 : _dreams.Length, count;
        for(int i = 0; i < length; ++i)
        {
            dreams = _dreams[i];
            count = dreams.dreams == null ? 0 : dreams.dreams.Length;
            if (count > result.index)
            {
                status = result.status;
                index = i;
                level = result.index;

                switch (result.status)
                {
                    case GameDreamerStatus.Sleep:
                        if (onSleep != null)
                            onSleep();
                        break;
                    case GameDreamerStatus.Dream:
                        if (onSleeping != null)
                            onSleeping();
                        break;
                    case GameDreamerStatus.Awake:
                        if (onAwake != null)
                            onAwake();
                        break;
                    default:
                        if (onAwaking != null)
                            onAwaking();
                        break;
                }

                break;
            }
            else
                result.index -= count;
        }
    }
}