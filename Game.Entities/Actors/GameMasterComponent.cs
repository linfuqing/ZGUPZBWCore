using System;
using Unity.Entities;
using ZG;

[EntityComponent(typeof(GameLevel))]
[EntityComponent(typeof(GameExp))]
[EntityComponent(typeof(GamePower))]
public class GameMasterComponent : EntityProxyComponent, IEntityComponent
{
#if UNITY_EDITOR
    [CSVField]
    public string 可选等级
    {
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                levelIndices = null;

                return;
            }

            var levels = value.Split('/');
            int numLevels = levels.Length;
            levelIndices = new int[numLevels];

            var temp = database.GetLevels();
            for (int i = 0; i < numLevels; ++i)
                levelIndices[i] = temp.IndexOf(levels[i]);
        }
    }

    public GameActorDatabase database;
#endif

    public int[] levelIndices;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameLevel level;
        level.handle = levelIndices[UnityEngine.Random.Range(0, levelIndices.Length)] + 1;

        assigner.SetComponentData(entity, level);
    }
}
