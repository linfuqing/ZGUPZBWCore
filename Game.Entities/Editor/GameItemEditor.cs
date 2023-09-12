using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System;
using ZG;

public class GameItemEditorObject : ScriptableObject
{
    public class World
    {
        private static List<GameItemHandle> __handles;
        private Dictionary<GameItemHandle, GameItemEditorObject> __instances;
        private Dictionary<int, GameItemEditorObject> __pool;

        private bool __Destroy(in GameItemHandle handle)
        {
            if (__instances == null || !__instances.TryGetValue(handle, out var instance))
                return false;

            __instances.Remove(handle);

            if (instance._parent != null)
                __Destroy(instance._parent.handle);

            if (instance._sibling != null)
                __Destroy(instance._sibling.handle);

            if (instance._children != null)
            {
                foreach(var child in instance._children)
                    __Destroy(child.handle);
            }

            if (__pool == null)
                __pool = new Dictionary<int, GameItemEditorObject>();

            __pool.Add(handle.index, instance);

            return true;
        }

        private GameItemEditorObject __Create(
            in GameItemHandle handle, 
            in GameItemManager.Hierarchy manager, 
            in SharedHashMap<Entity, Entity>.Reader handleEntities)
        {
            if (!manager.GetChildren(handle, out var enumerator, out var item))
            {
                if (!handle.Equals(GameItemHandle.Empty))
                    Debug.LogError($"Erorr Handle {handle}");

                return null;
            }

            GameItemEditorObject instance;
            if (__instances == null)
                __instances = new Dictionary<GameItemHandle, GameItemEditorObject>();
            else if (__instances.TryGetValue(handle, out instance))
                return instance;

            if (__pool != null && __pool.TryGetValue(handle.index, out instance))
                __pool.Remove(handle.index);
            else
                instance = CreateInstance<GameItemEditorObject>();
            
            __instances[handle] = instance;

            instance.name = handle.ToString();

            if (__handles == null)
                __handles = new List<GameItemHandle>();
            else
                __handles.Clear();

            while (enumerator.MoveNext())
                __handles.Add(enumerator.Current.handle);

            var children = __handles.Count > 0 ? __handles.ToArray() : null;

            instance._index = handle.index;
            instance._version = item.version;
            instance._type = item.type;
            instance._count = item.count;
            instance._parentChildIndex = item.parentChildIndex;
            instance._entity = handleEntities[GameItemStructChangeFactory.Convert(handle)];
            instance._parent = __Create(item.parentHandle, manager, handleEntities);
            instance._sibling = __Create(item.siblingHandle, manager, handleEntities);

            int numChildren = children == null ? 0 : children.Length;
            instance._children = numChildren > 0 ? new GameItemEditorObject[numChildren] : null;
            for (int i = 0; i < numChildren; ++i)
                instance._children[i] = __Create(children[i], manager, handleEntities);

            return instance;
        }

        public GameItemEditorObject[] Create(
            in NativeArray<GameItemHandle> handles, 
            in GameItemManager.Hierarchy manager, 
            in SharedHashMap<Entity, Entity>.Reader handleEntities)
        {
            if (__instances != null)
            {
                if (__pool == null)
                    __pool = new Dictionary<int, GameItemEditorObject>();

                foreach(var instance in __instances)
                    __pool.Add(instance.Key.index, instance.Value);

                __instances.Clear();
            }

            int numHandles = handles.Length;
            var results = new GameItemEditorObject[numHandles];
            for (int i = 0; i < numHandles; ++i)
                results[i] = __Create(handles[i], manager, handleEntities);

            return results;
        }
    }

    [SerializeField]
    internal int _index;
    [SerializeField]
    internal int _version;
    [SerializeField]
    internal int _type;
    [SerializeField]
    internal int _count;

    [SerializeField]
    internal int _parentChildIndex;
    [SerializeField]
    internal Entity _entity;
    [SerializeField]
    internal GameItemEditorObject _parent;
    [SerializeField]
    internal GameItemEditorObject _sibling;
    [SerializeField]
    internal GameItemEditorObject[] _children;

    private static Dictionary<string, World> __worlds;

    public GameItemHandle handle
    {
        get
        {
            GameItemHandle value;
            value.index = _index;
            value.version = _version;

            return value;
        }
    }

    public static World GetOrCreateWorld(string worldName)
    {
        if (__worlds == null)
            __worlds = new Dictionary<string, World>();

        if(!__worlds.TryGetValue(worldName, out var world))
        {
            world = new World();

            __worlds[worldName] = world;
        }

        return world;
    }

    public override string ToString()
    {
        return handle.ToString();
    }
}

public class GameItemEditor : EditorWindow
{
    private struct Comparer : IComparer<GameItemHandle>
    {
        public int Compare(GameItemHandle x, GameItemHandle y)
        {
            return x.index.CompareTo(y.index);
        }
    }

    private struct Search : IJobParallelFor
    {
        public int index;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        public NativeList<GameItemHandle>.ParallelWriter handles; 

        public void Execute(int index)
        {
            if (!IsSubSegment(index, this.index))
                return;

            if (!itemManager.TryGetValue(index, out var item))
                return;

            GameItemHandle handle;
            handle.index = index;
            handle.version = item.version;
            handles.AddNoResize(handle);
        }

        public static int GetHighestDigit(int value, int binary = 10)
        {
            int result = 0;
            while(value != 0)
            {
                ++result;

                value /= binary;
            }

            return result;
        }

        public static int Round(int value, int digit, int binary = 10)
        {
            digit *= binary;

            value /= digit;

            return value * digit;
        }

        public static bool IsMantissa(int x, int y, int binary = 10)
        {
            if (y == 0)
                return false;

            return Round(x, GetHighestDigit(y), binary) == (x > 0 ? x - y : x + y);
        }

        public static bool IsSubSegment(int x, int y, int binary = 10)
        {
            if (x == y)
                return true;

            if (y < 0)
            {
                if (x >= 0)
                    return false;
            }
            else if (x < 0)
                    x = -x;

            if (x < y)
                return false;

            while(x != 0)
            {
                if (IsMantissa(x, y, binary))
                    return true;

                x /= binary;
            }

            return false;
        }
    }

    private int __worldIndex;
    private string __searchText = "Search..";
    private JobHandle __jobHandle;
    private NativeList<GameItemHandle> __handles;
    private ReorderableList __reorderableList;

    [MenuItem("Window/Game/Item Editor")]
    public static void ShowWindow()
    {
        GetWindow<GameItemEditor>();
    }

    void OnGUI()
    {
        var worlds = World.All;
        int numWorlds = worlds.Count;
        if (numWorlds < 1)
            return;

        var worldNames = new string[numWorlds];
        for (int i = 0; i < numWorlds; ++i)
            worldNames[i] = worlds[i].Name;

        __worldIndex = EditorGUILayout.Popup("World", __worldIndex, worldNames);
        var world = worlds[__worldIndex].Unmanaged;
        var itemSystem = world.GetExistingUnmanagedSystem<GameItemSystem>();
        if (itemSystem == SystemHandle.Null)
            return;

        __searchText = EditorGUILayout.DelayedTextField(__searchText);

        if (__jobHandle.IsCompleted)
        {
            __jobHandle.Complete();

            var itemManagerShared = world.GetUnsafeSystemRef<GameItemSystem>(itemSystem).manager;

            ref var lookupJobManager = ref itemManagerShared.lookupJobManager;
            lookupJobManager.CompleteReadOnlyDependency();

            var itemManager = itemManagerShared.value;
            if (__handles.IsCreated)
            {
                var handleEntities = world.EntityManager.GetComponentData<GameItemStructChangeManager>(world.GetExistingUnmanagedSystem<GameItemStructChangeSystem>()).handleEntities;
                handleEntities.lookupJobManager.CompleteReadOnlyDependency();

                var objects = GameItemEditorObject.GetOrCreateWorld(worldNames[__worldIndex]).Create(
                    __handles.AsArray(), 
                    itemManager.hierarchy, 
                    handleEntities.reader);
                __reorderableList = new ReorderableList(objects, typeof(GameItemEditorObject));
                __reorderableList.displayAdd = false;
                __reorderableList.displayRemove = false;
                __reorderableList.draggable = false;
                __reorderableList.drawHeaderCallback += rect =>
                {
                    EditorGUI.LabelField(rect, __jobHandle.IsCompleted ? "Completed" : "Refresh..");
                };

                __reorderableList.onSelectCallback += x =>
                {
                    var selectedIndices = x.selectedIndices;
                    int numSelectedIndices = selectedIndices == null ? 0 : selectedIndices.Count;
                    switch (numSelectedIndices)
                    {
                        case 0:
                            
                            break;
                        case 1:
                            Selection.activeObject = objects[selectedIndices[0]];
                            break;
                        default:
                            var selectedObjects = new UnityEngine.Object[numSelectedIndices];
                            for (int i = 0; i < numSelectedIndices; ++i)
                                selectedObjects[i] = objects[selectedIndices[i]];

                            Selection.objects = selectedObjects;

                            break;
                    }
                };
            }

            if (int.TryParse(__searchText, out int index))
            {
                int length = itemManager.length;

                if (__handles.IsCreated)
                    __handles.Clear();
                else
                    __handles = new NativeList<GameItemHandle>(Allocator.Persistent);

                __handles.Capacity = Math.Max(__handles.Capacity, length);

                var sortJob = __handles.SortJob(new Comparer());

                Search search;
                search.index = index;
                search.itemManager = itemManager.readOnly;
                search.handles = __handles.AsParallelWriter();
                __jobHandle = search.ScheduleByRef(length, 32);

                lookupJobManager.AddReadOnlyDependency(__jobHandle);

                __jobHandle = sortJob.Schedule(__jobHandle);

                JobHandle.ScheduleBatchedJobs();
            }
        }

        if (__reorderableList != null)
            __reorderableList.DoLayoutList();
    }
}
