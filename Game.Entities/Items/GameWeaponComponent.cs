using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

[assembly: RegisterEntityObject(typeof(GameWeaponComponent))]

public struct GameWeaponResult
{
    public float value;
    public GameItemHandle handle;
}

public struct GameWeaponFunctionWrapper : IFunctionWrapper
{
    public GameWeaponResult result;
    public EntityObject<GameWeaponComponent> value;

    public void Invoke()
    {
        var component = value.value;
        if (component != null)
            component._OnChanged(result);
    }
}

[EntityComponent]
//[EntityComponent(typeof(GameWeaponCallback))]
public class GameWeaponComponent : MonoBehaviour
{
    public event Action<GameItemHandle, float> onDamage;

    internal void _OnChanged(GameWeaponResult result)
    {
        if (this == null)
            return;

        if (math.abs(result.value) > math.FLT_MIN_NORMAL && onDamage != null)
            onDamage(result.handle, result.value);
    }
}