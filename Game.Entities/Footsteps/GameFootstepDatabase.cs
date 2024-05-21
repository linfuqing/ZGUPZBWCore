using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using ZG;

[CreateAssetMenu(fileName = "Game Footstep Database", menuName = "Game/GameFootstepDatabase")]
public class GameFootstepDatabase : ScriptableObject, ISerializationCallbackReceiver
{
    [Serializable]
    public struct Tag
    {
        public string name;

        public string animatorControllerEventType;

        public MeshInstanceHybridAnimatorEventConfigBase hybridAnimatorEventOverride;

        //Event state or particle index
        public int state;

        public LayerMask layerMask;

        [Tooltip("�������ţ�ʵ������+1��������0��ʵ��Ϊ1")]
        public float scale;

        [Tooltip("�����ٶ����������ӣ��볣�����ŵ���")]
        public float scalePerSpeed;

        [Tooltip("�����ٶ��������Ӧ����������")]
        public float countPerSpeed;

        [Tooltip("��С�ٶ����ƣ����ٶ����Ʒ�Χ�ڸ������Ч")]
        public float minSpeed;

        [Tooltip("����ٶ����ƣ����ٶ����Ʒ�Χ�ڸ������Ч")]
        public float maxSpeed;

        [Tooltip("��д���Animator����ȡSpeed������������ݶ���Y���ٶ��Զ�����")]
        public string animatorControllerSpeedParameter;

        public void ToAsset(ref GameFootstepDefinition.Tag tag)
        {
            tag.state = state;
            tag.layerMask = (uint)layerMask.value;
            tag.scale = scale;
            tag.scalePerSpeed = scalePerSpeed;
            tag.countPerSpeed = countPerSpeed;
            tag.minSpeed = minSpeed;
            tag.maxSpeed = maxSpeed;
            tag.eventType = hybridAnimatorEventOverride == null ? animatorControllerEventType : hybridAnimatorEventOverride.typeName;
            tag.speedParamter = animatorControllerSpeedParameter;
        }
    }

    [Serializable]
    public struct Foot
    {
        public string bonePath;

        [Tooltip("̧�ŵ���С�߶ȣ���������߶ȿ������¿�ʼ�ж��Ƿ����")]
        public float minPlaneHeight;

        [Tooltip("��ŵ����߶ȣ�С�ڸø߶��ҳ�����С�ٶ��ж�Ϊ��ӡ")]
        public float maxPlaneHeight;

        public Tag[] tags;

        public void ToAsset(
            BlobBuilder blobBuilder, 
            in MeshInstanceRigDatabase.Rig dataRig, 
            ref GameFootstepDefinition.Foot foot)
        {
            foot.boneIndex = dataRig.BoneIndexOf(bonePath);
            if(foot.boneIndex == -1)
                Debug.LogError($"Bone path {bonePath} can not been found!");

            foot.minPlaneHeight = minPlaneHeight;
            foot.maxPlaneHeight = maxPlaneHeight;

            int numTags = this.tags.Length;
            var tags = blobBuilder.Allocate(ref foot.tags, numTags);
            for (int i = 0; i < numTags; ++i)
                this.tags[i].ToAsset(ref tags[i]);
        }
    }

    [Serializable]
    public struct Rig
    {
        public int index;
        public Foot[] foots;

        public void ToAsset(
            BlobBuilder blobBuilder,
            in MeshInstanceRigDatabase.Rig[] dataRigs,
            ref GameFootstepDefinition.Rig rig)
        {
            var dataRig = dataRigs[index];

            rig.index = index;

            int numFoots = this.foots.Length;
            var foots = blobBuilder.Allocate(ref rig.foots, numFoots);
            for (int i = 0; i < numFoots; ++i)
                this.foots[i].ToAsset(blobBuilder, dataRig, ref foots[i]);
        }
    }

    [Serializable]
    public struct Data
    {
        public Rig[] rigs;

        public BlobAssetReference<GameFootstepDefinition> ToAsset(in MeshInstanceRigDatabase.Rig[] dataRigs)
        {
            using(var blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref blobBuilder.ConstructRoot<GameFootstepDefinition>();

                int numRigs = this.rigs == null ? 0 : this.rigs.Length;
                var rigs = blobBuilder.Allocate(ref root.rigs, numRigs);
                for (int i = 0; i < numRigs; ++i)
                    this.rigs[i].ToAsset(blobBuilder, dataRigs, ref rigs[i]);

                return blobBuilder.CreateBlobAssetReference<GameFootstepDefinition>(Allocator.Persistent);
            }
        }
    }

    [HideInInspector]
    [SerializeField]
    private byte[] __bytes;

    private BlobAssetReference<GameFootstepDefinition> __definition;

    public BlobAssetReference<GameFootstepDefinition> definition => __definition;

    ~GameFootstepDatabase()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (__definition.IsCreated)
        {
            __definition.Dispose();

            __definition = BlobAssetReference<GameFootstepDefinition>.Null;
        }
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (__bytes != null && __bytes.Length > 0)
        {
            if (__definition.IsCreated)
                __definition.Dispose();

            unsafe
            {
                fixed (byte* ptr = __bytes)
                {
                    using (var reader = new MemoryBinaryReader(ptr, __bytes.LongLength))
                    {
                        __definition = reader.Read<GameFootstepDefinition>();
                    }
                }
            }

            __bytes = null;
        }
    }

    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
        if (__definition.IsCreated)
        {
            using (var writer = new MemoryBinaryWriter())
            {
                writer.Write(__definition);

                __bytes = writer.GetContentAsNativeArray().ToArray();
            }
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

#if UNITY_EDITOR
    public MeshInstanceRigDatabase database;

    public Data data;

    public void Rebuild()
    {
        Dispose();

        __definition = data.ToAsset(database.data.rigs);

        ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
    }

    public void EditorMaskDirty()
    {
        if (Application.isPlaying)
            return;

        Rebuild();

        UnityEditor.EditorUtility.SetDirty(this);
    }

    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += EditorMaskDirty;
    }
#endif
}
