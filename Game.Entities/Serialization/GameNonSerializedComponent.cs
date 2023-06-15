using Unity.Entities;
using UnityEngine;
using ZG;

public struct GameNonSerialized : IComponentData
{
}

[EntityComponent(typeof(GameNonSerialized))]
public class GameNonSerializedComponent : MonoBehaviour
{
}
