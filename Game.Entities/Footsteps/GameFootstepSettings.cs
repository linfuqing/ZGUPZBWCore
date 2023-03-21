using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameFootstepSettings : MonoBehaviour, IGameFootstepResources
{
    [SerializeField]
    internal GameFootstepResources _resources;

    private ParticleSystem[] __particleSystems;

    private static GameFootstepSettings __instance;

    public static IGameFootstepResources resources
    {
        get
        {
            return __instance;
        }
    }

    public int particleSystemCount => _resources.particleSystemCount;

    public ParticleSystem LoadParticleSystem(int index)
    {
        var particleSystem = __particleSystems == null ? null : __particleSystems[index];
        if (particleSystem == null)
        {
#if UNITY_EDITOR
            if (GameAssetManager.instance == null)
            {
                var asset = _resources.particleSystemAssets[index];
                GameObject gameObject;
                var paths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(asset.label, asset.name);
                foreach (var path in paths)
                {
                    gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    particleSystem = gameObject == null ? null : gameObject.GetComponentInChildren<ParticleSystem>();
                }
            }
            else
#endif
                particleSystem = _resources.LoadParticleSystem(index);

            if (particleSystem != null)
            {
                particleSystem = Instantiate(particleSystem);

                if (__particleSystems == null)
                    __particleSystems = new ParticleSystem[particleSystemCount];

                __particleSystems[index] = particleSystem;
            }
        }

        return particleSystem;
    }

    void OnEnable()
    {
        __instance = this;
    }

    void OnDisable()
    {
        if(__instance == this)
            __instance = null;
    }

    void OnDestroy()
    {
        ParticleSystem particleSystem;
        int particleSystemCount = __particleSystems == null ? 0 : __particleSystems.Length;
        for (int i = 0; i < particleSystemCount; ++i)
        {
            particleSystem = __particleSystems[i];
            if (particleSystem != null)
                Destroy(particleSystem.gameObject);
        }

        __particleSystems = null;
    }
}