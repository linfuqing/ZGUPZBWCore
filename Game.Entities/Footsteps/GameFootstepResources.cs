using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Game Footstep Resources", fileName = "GameFootstepResources")]
public class GameFootstepResources : ScriptableObject, IGameFootstepResources
{
    [Serializable]
    public struct Asset
    {
#if UNITY_EDITOR
        public string title;
#endif

        public string label;
        public string name;
    }

    public Asset[] particleSystemAssets;

    public int particleSystemCount => particleSystemAssets.Length;

    public ParticleSystem LoadParticleSystem(int index)
    {
        var particleSystemAsset = particleSystemAssets[index];

        var gameObject = GameAssetManager.instance.dataManager.Load<GameObject>(particleSystemAsset.label, particleSystemAsset.name);

        return gameObject == null ? null : gameObject.GetComponentInChildren<ParticleSystem>(true);
    }
}
