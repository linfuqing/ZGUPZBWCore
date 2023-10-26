using Unity.Entities;
using UnityEngine;
using ZG;

[EntityComponent(typeof(GameItemSibling))]
[EntityComponent(typeof(GameFormulaFactoryMode))]
[EntityComponent(typeof(GameFormulaFactoryStatus))]
[EntityComponent(typeof(GameFormulaFactoryTime))]
[EntityComponent(typeof(GameFormulaFactoryTimeScale))]
[EntityComponent(typeof(GameFormulaFactoryItemTimeScale))]
[EntityComponent(typeof(GameFormulaFactoryCommand))]
[EntityComponent(typeof(GameFormulaFactoryInstance))]
[EntityComponent(typeof(GameFormulaFactoryStorage))]
public class GameFormulaFactoryComponent : EntityProxyComponent, IEntityComponent
{
#if UNITY_EDITOR
    public UnityEngine.Object database;
#endif

    [SerializeField]
    public GameFormulaFactoryMode.Mode _mode;
    [SerializeField]
    public GameFormulaFactoryMode.OwnerType _ownerType;
    [SerializeField, Index("database.combines", emptyName = "Null", pathLevel = -1)]
    public int _formulaIndex = -1;
    [SerializeField]
    public float _time = 0.0f;
    [SerializeField]
    public float _timeScale = 1.0f;

    public bool isStorageActive
    {
        set
        {
            GameFormulaFactoryStorage storage;
            storage.status = value ? GameFormulaFactoryStorage.Status.Active : GameFormulaFactoryStorage.Status.Invactive;
            this.SetComponentData(storage);
        }
    }

    public bool hasTime => _time > 0.0f || _formulaIndex != -1;

    public GameFormulaFactoryStatus status
    {
        get
        {
            return this.GetComponentData<GameFormulaFactoryStatus>();
        }
    }

    public float time
    {
        get
        {
            if (this.TryGetComponentData(out GameFormulaFactoryTime time))
                return time.value;

            return 0.0f;
        }
    }

    public float timeScale
    {
        get
        {
            if (this.TryGetComponentData(out GameFormulaFactoryTimeScale timeScale))
                return timeScale.value;

            return _timeScale;
        }

        set
        {
            if (gameObjectEntity.isAssigned)
            {
                GameFormulaFactoryTimeScale timeScale;
                timeScale.value = value;
                this.SetComponentData(timeScale);
            }
        }
    }

    public float timeScaleOrigin => _timeScale;

    /*[EntityComponents]
    public Type[] entityComponentTypes
    {
        get
        {
            if (hasTime)
                return new Type[] { typeof(GameFormulaFactoryTime) };

            return null;
        }
    }*/

    public void ChangeTimeScale(float value)
    {
        timeScale += value;
    }

    public void Command(in Entity entity, int formulaIndex)
    {
        GameFormulaFactoryCommand command;
        command.entity = entity;
        command.formulaIndex = formulaIndex;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameFormulaFactoryCommand>(true);
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameFormulaFactoryMode mode;
        mode.value = _mode;
        mode.ownerType = _ownerType;
        assigner.SetComponentData(entity, mode);

        GameFormulaFactoryStatus status;
        status.value = hasTime ? GameFormulaFactoryStatus.Status.Running : GameFormulaFactoryStatus.Status.Normal;
        status.formulaIndex = _formulaIndex;
        status.level = 0;
        status.count = 0;
        status.entity = Entity.Null;
        assigner.SetComponentData(entity, status);

        if(_time > 0.0f)
        {
            GameFormulaFactoryTime time;
            time.value = _time;
            assigner.SetComponentData(entity, time);
        }
        else if(_formulaIndex == -1)
            assigner.SetComponentEnabled<GameFormulaFactoryTime>(entity, false);

        if (_timeScale > 0.0f)
        {
            GameFormulaFactoryTimeScale timeScale;
            timeScale.value = _timeScale;
            assigner.SetComponentData(entity, timeScale);
        }
    }
}
