using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Collections;
using UnityEngine;
using ZG;
using FixedString = Unity.Collections.FixedString32Bytes;

[CreateAssetMenu(menuName = "Game/Game Physics Hierarchy Database", fileName = "GamePhysicsHierarchyDatabase")]
public class GamePhysicsHierarchyDatabase : ScriptableObject, ISerializationCallbackReceiver
{
    public const int VERSION = 0;

    [SerializeField, HideInInspector]
    private byte[] __bytes;

    private BlobAssetReference<GamePhysicsHierarchyDefinition> __definition;

    public BlobAssetReference<GamePhysicsHierarchyDefinition> definition
    {
        get
        {
            return __definition;
        }
    }

    ~GamePhysicsHierarchyDatabase()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (__definition.IsCreated)
        {
            __definition.Dispose();

            __definition = BlobAssetReference<GamePhysicsHierarchyDefinition>.Null;
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
                        int version = reader.ReadInt();

                        UnityEngine.Assertions.Assert.AreEqual(VERSION, version);

                        __definition = reader.Read<GamePhysicsHierarchyDefinition>();
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
                writer.Write(VERSION);
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
    [Serializable]
    public struct Node
    {
        [Tooltip("作为父级时子集结构应该删除的吸附点。")]
        public string[] parentChildNames;

        [Tooltip("当前结构应该删除的吸附点。")]
        public int[] childIndices;
    }

    public Node[] nodes;

    [Serializable]
    public struct Data
    {
        [Serializable]
        public struct Shape
        {
            public string name;

            [Tooltip("作为父级时子集结构应该删除的吸附点。")]
            public string[] childShapeNames;

            [Tooltip("当前结构应该删除的吸附点。")]
            public string[] shapeNames;
        }

        [Tooltip("手填项")]
        public Shape[] shapes;

        [Tooltip("无需手填")]
        public int[] childShapeIndices;

        public static void Create(IPhysicsHierarchyShape shape, Transform root, Shape[] shapes, ref List<int> childShapeIndices)
        {
            if (root.childCount > 0)
            {
                IPhysicsHierarchyShape childShape;
                foreach (Transform child in root)
                {
                    childShape = child.GetComponent<IPhysicsHierarchyShape>();
                    if (childShape == null)
                        childShape = shape;
                    else /*if (!child.gameObject.activeInHierarchy)
                        continue;*/

                    Create(childShape, child, shapes, ref childShapeIndices);
                }
            }
            
            int childShapeIndex = -1;
            string name = shape == null ? null : shape.name;
            if (name != null)
            {
                int i, numShapes = shapes == null ? 0 : shapes.Length;
                for (i = 0; i < numShapes; ++i)
                {
                    if (shapes[i].name == name)
                        break;
                }

                if (i < numShapes)
                    childShapeIndex = i;
                else
                    Debug.LogError($"Shape {name} has not been found!");
            }

            if (childShapeIndices == null)
                childShapeIndices = new List<int>();

            childShapeIndices.Add(childShapeIndex);
        }

        public BlobAssetReference<GamePhysicsHierarchyDefinition> ToAsset()
        {
            using(var blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref var definition = ref blobBuilder.ConstructRoot<GamePhysicsHierarchyDefinition>();

                int i, j, k, numChildShapeNames, numShapeNames, numShapes = this.shapes == null ? 0 : this.shapes.Length;
                string shapeName;
                var shapes = blobBuilder.Allocate(ref definition.shapes, numShapes);
                BlobBuilderArray<FixedString> childShapeNames;
                BlobBuilderArray<int> shapeIndices;
                for (i = 0; i < numShapes; ++i)
                {
                    ref readonly var sourceShape = ref this.shapes[i];
                    ref var destinationShape = ref shapes[i];

                    destinationShape.name = sourceShape.name;

                    numChildShapeNames = sourceShape.childShapeNames == null ? 0 : sourceShape.childShapeNames.Length;
                    childShapeNames = blobBuilder.Allocate(ref destinationShape.childShapeNames, numChildShapeNames);
                    for (j = 0; j < numChildShapeNames; ++j)
                        childShapeNames[j] = sourceShape.childShapeNames[j];

                    numShapeNames = sourceShape.shapeNames == null ? 0 : sourceShape.shapeNames.Length;
                    shapeIndices = blobBuilder.Allocate(ref destinationShape.shapeIndices, numShapeNames);
                    for (j = 0; j < numShapeNames; ++j)
                    {
                        shapeName = sourceShape.shapeNames[j];
                        for(k = 0; k < numShapes; ++k)
                        {
                            if(this.shapes[k].name == shapeName)
                            {
                                shapeIndices[j] = k;

                                break;
                            }
                        }

                        if (k == numShapes)
                        {
                            Debug.LogError($"Error ShapeName {shapeName} From Shape {sourceShape.name}");

                            shapeIndices[j] = -1;
                        }
                    }
                }

                int numChildShapeIndices = this.childShapeIndices == null ? 0 : this.childShapeIndices.Length;
                var childShapeIndices = blobBuilder.Allocate(ref definition.childShapeIndices, numChildShapeIndices);
                for (i = 0; i < numChildShapeIndices; ++i)
                    childShapeIndices[i] = this.childShapeIndices[i];

                return blobBuilder.CreateBlobAssetReference<GamePhysicsHierarchyDefinition>(Allocator.Persistent);
            }
        }
    }

    public Data data;

    [HideInInspector]
    public Transform root;

    public void Create()
    {
        /////
        int numShapes = nodes == null ? 0 : nodes.Length;
        if (numShapes > 0)
        {
            var shapes = new List<Data.Shape>();
            Data.Shape destinationShape;
            int shapeIndex = 0, numChildIndices, i;
            foreach(Transform child in root)
            {
                if (child.gameObject.activeSelf)
                {
                    ref var sourceShape = ref nodes[shapeIndex];
                    //ref var destinationShape = ref data.shapes[shapeIndex];

                    destinationShape.name = child.name;

                    destinationShape.childShapeNames = sourceShape.parentChildNames;

                    numChildIndices = sourceShape.childIndices == null ? 0 : sourceShape.childIndices.Length;

                    destinationShape.shapeNames = new string[numChildIndices];
                    for (i = 0; i < numChildIndices; ++i)
                        destinationShape.shapeNames[i] = root.GetChild(sourceShape.childIndices[i]).name;

                    shapes.Add(destinationShape);
                }

                ++shapeIndex;
            }

            data.shapes = shapes.ToArray();
        }
        /////
        

        List<int> childShapeIndices = null;

        Data.Create(root.GetComponentInParent<IPhysicsHierarchyShape>(), root, data.shapes, ref childShapeIndices);

        data.childShapeIndices = childShapeIndices == null ? null : childShapeIndices.ToArray();
    }

    public void Rebuild()
    {
        if (__definition.IsCreated)
            __definition.Dispose();

        __definition = data.ToAsset();

        __bytes = null;

        ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
    }

    public void EditorMaskDirty()
    {
        Rebuild();

        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
