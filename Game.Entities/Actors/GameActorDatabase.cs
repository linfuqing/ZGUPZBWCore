using System;
using UnityEngine;
using ZG;

public abstract class GameActorDatabase : ScriptableObject
{
    public const int PROPERTY_COUNT = 9;

#if UNITY_EDITOR
    public abstract System.Collections.IEnumerable GetActions();

    public abstract System.Collections.IEnumerable GetAssets();

    public abstract System.Collections.IEnumerable GetFoods();

    public abstract System.Collections.IEnumerable GetLevels();
#endif

}
