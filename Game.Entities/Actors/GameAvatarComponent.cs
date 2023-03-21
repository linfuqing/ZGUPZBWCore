using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using ZG;

[InternalBufferCapacity(4)]
public struct GameAvatarItem : IBufferElementData
{
    public int index;

    public static implicit operator GameAvatarItem(int x)
    {
        GameAvatarItem item;
        item.index = x;
        return item;
    }
}

[EntityComponent(typeof(GameAvatarItem))]
public class GameAvatarComponent : MonoBehaviour
{
}
