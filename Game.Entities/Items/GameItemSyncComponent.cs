using Unity.Entities;
using UnityEngine;
using ZG;

public struct GameItemSync : IComponentData
{

}

public struct GameItemSyncDisabled : IComponentData
{

}

[EntityComponent(typeof(GameItemSync))]
[EntityComponent(typeof(GameItemSyncDisabled))]
public class GameItemSyncComponent : MonoBehaviour
{
}
