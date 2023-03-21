using System;
using Unity.Entities;
using ZG;

[DisableAutoCreation]
public partial class GameRollbackManagedSystem : SystemBase
{
    public RollbackContainerManager containerManager
    {
        get;
    } = new RollbackContainerManager();

    protected override void OnDestroy()
    {
        containerManager.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        throw new NotImplementedException();
    }
}
