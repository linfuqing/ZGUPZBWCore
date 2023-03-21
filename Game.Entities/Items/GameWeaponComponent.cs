using System;
using Unity.Entities;
using Unity.Mathematics;
using ZG;

//[assembly: RegisterEntityObject(typeof(GameWeaponComponent))]

[Serializable]
public struct GameWeaponResult
{
    public float value;
    public GameItemHandle handle;
}

[Serializable]
public struct GameWeaponCallback : ICleanupComponentData
{
    public CallbackHandle<GameWeaponResult> value;
}

//[EntityComponent]
[EntityComponent(typeof(GameWeaponCallback))]
public class GameWeaponComponent : EntityProxyComponent, IEntitySystemStateComponent
{
    public event Action<GameItemHandle, float> onDamage;

    private void __Damage(GameWeaponResult result)
    {
        if (this == null)
            return;

        if (math.abs(result.value) > math.FLT_MIN_NORMAL && onDamage != null)
            onDamage(result.handle, result.value);
    }

    void IEntitySystemStateComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameWeaponCallback callback;
        callback.value = new Action<GameWeaponResult>(__Damage).Register();
        assigner.SetComponentData(entity, callback);
    }

}