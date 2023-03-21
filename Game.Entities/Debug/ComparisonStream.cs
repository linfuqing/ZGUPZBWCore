using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using ZG;

public struct ComparisonStream<T> where T : unmanaged, IEquatable<T>
{
    private NativeHashStream<T>.Writer __writer;

    public ComparisonStream(NativeHashStream<T>.Writer writer)
    {
        __writer = writer;
    }

    public bool TryBegin(T value)
    {
        return __writer.TryBegin(value);
    }

    public void Begin(T value)
    {
        __writer.Begin(value);
    }

    public void End()
    {
        __writer.End();
    }

    public void Assert<U>(FixedString32Bytes name, U value) where U : unmanaged
    {
        __writer.Write(name);
        __writer.Write(UnsafeUtility.SizeOf<U>());
        __writer.Write(value);
    }
}

public partial class ComparisionSystem<T> : SystemBase where T : unmanaged, IEquatable<T>
{
    public struct StreamScheduler
    {
        private string __systemName;
        private string __worldName;
        private Frame __frame;

        internal StreamScheduler(
            string systemName, 
            string worldName, 
            Frame frame)
        {
            __systemName = systemName;
            __worldName = worldName;
            __frame = frame;
        }

        public ComparisonStream<T> Begin(int foreachCount)
        {
            if (foreachCount < 1)
                foreachCount = 1;

            return __frame.Begin(foreachCount, __systemName, __worldName);
        }

        public void End(JobHandle jobHandle)
        {
            __frame.End(__systemName, __worldName, jobHandle);
        }
    }

    internal class Frame
    {
        private struct Stream
        {
            public JobHandle jobHandle;
            public NativeHashStream<T> value;
        }

        private struct Reader
        {
            public string name;
            public NativeHashStream<T>.Reader value;
            public bool isReadable;
        }

        private uint __frameIndex;
        private Queue<NativeHashStream<T>> __pool;
        private Dictionary<string, Dictionary<string, Stream>> __streams;

        public static string Dump(string systemName, string worldName, string sourceName, string destinationName, T value, uint frameIndex)
        {
            if (sourceName == destinationName)
                return systemName + ":" + worldName + ":" + (destinationName ?? string.Empty) + ":" + value + ":" + frameIndex;

            return systemName + ":" + worldName + ":" + (destinationName ?? string.Empty) + ":" + sourceName + ":" + value + ":" + frameIndex;
        }

        public Frame(uint frameIndex, Queue<NativeHashStream<T>> pool)
        {
            __frameIndex = frameIndex;
            __pool = pool;
        }

        public void Remove(string worldName)
        {
            if(__streams != null)
            {
                Stream stream;
                foreach (var streams in __streams.Values)
                {
                    if (streams.TryGetValue(worldName, out stream))
                    {
                        stream.jobHandle.Complete();

                        __pool.Enqueue(stream.value);

                        streams.Remove(worldName);
                    }
                }
            }
        }

        public void Remove(string systemName, string worldName)
        {
            if (__streams != null && __streams.TryGetValue(systemName, out var streams) && streams.ContainsKey(worldName))
                Remove(worldName);
        }

        public ComparisonStream<T> Begin(int foreachCount, string systemName, string worldName)
        {
            if (__streams == null)
                __streams = new Dictionary<string, Dictionary<string, Stream>>();

            if (!__streams.TryGetValue(systemName, out var streams))
            {
                streams = new Dictionary<string, Stream>();

                __streams[systemName] = streams;
            }

            if (streams.TryGetValue(worldName, out Stream stream))
            {
                stream.jobHandle.Complete();
                stream.value.Reset(foreachCount);
            }
            else if (__pool.Count > 0)
            {
                stream.value = __pool.Dequeue();
                stream.value.Reset(foreachCount);
            }
            else
                stream.value = new NativeHashStream<T>(foreachCount, Allocator.Persistent);

            stream.jobHandle = default;
            streams[worldName] = stream;
            
            return new ComparisonStream<T>(stream.value.writer);
        }

        public void End(string systemName, string worldName, JobHandle jobHandle)
        {
            var steams = __streams[systemName];
            var steam = steams[worldName];
            steam.jobHandle = jobHandle;
            steams[worldName] = steam;
        }

        public unsafe void Dump()
        {
            bool isRaiseExceptions = UnityEngine.Assertions.Assert.raiseExceptions;
            UnityEngine.Assertions.Assert.raiseExceptions = false;

            int maxForeachCount, maxRenderIndex, numRenderCount, index, sourceSize, destinationSize, i;
            void* sourceBytes, destinationBytes;
            Stream stream;
            Reader reader = default;
            string sourceName, destinationName;
            Dictionary<string, Stream> streams;
            Reader[] readers;
            foreach (var pair in __streams)
            {
                streams = pair.Value;
                numRenderCount = streams.Count;
                readers = new Reader[numRenderCount];
                index = 0;
                maxForeachCount = 0;
                maxRenderIndex = 0;
                foreach (var temp in pair.Value)
                {
                    stream = temp.Value;
                    stream.jobHandle.Complete();

                    reader.name = temp.Key;
                    reader.value = stream.value.reader;
                    readers[index] = reader;

                    i = stream.value.foreachCount;
                    if (maxForeachCount < i)
                    {
                        maxForeachCount = i;

                        maxRenderIndex = index;
                    }

                    ++index;
                }

                if (maxForeachCount > 0)
                {
                    reader = readers[maxRenderIndex];
                    readers[maxRenderIndex] = readers[--numRenderCount];
                    using (var keys = reader.value.GetKeys(Allocator.Temp))
                    {
                        foreach (var key in keys)
                        {
                            reader.value.Begin(key);
                            for (i = 0; i < numRenderCount; ++i)
                            {
                                ref Reader temp = ref readers[i];
                                temp.isReadable = temp.value.TryBegin(key);
                            }

                            index = 0;
                            while (reader.value.remainingItemCount > 0)
                            {
                                sourceName = reader.value.Read<FixedString32Bytes>().ToString();
                                sourceSize = reader.value.Read<int>();
                                sourceBytes = reader.value.Read(sourceSize);
                                for (i = 0; i < numRenderCount; ++i)
                                {
                                    ref Reader temp = ref readers[i];
                                    if (!temp.isReadable || temp.value.remainingItemCount < 1)
                                    {
                                        if(temp.isReadable)
                                            Debug.LogError(Dump(pair.Key, temp.name, sourceName, null, key, __frameIndex));

                                        continue;
                                    }

                                    destinationName = temp.value.Read<FixedString32Bytes>().ToString();
                                    destinationSize = temp.value.Read<int>();
                                    destinationBytes = temp.value.Read(destinationSize);
                                    
                                    UnityEngine.Assertions.Assert.IsTrue(sourceName == destinationName, Dump(pair.Key, temp.name, sourceName, destinationName, key, __frameIndex));
                                    UnityEngine.Assertions.Assert.IsTrue(sourceSize == destinationSize, Dump(pair.Key, temp.name, sourceName, destinationName, key, __frameIndex));
                                    if (sourceName == destinationName && sourceSize == destinationSize)
                                    {
                                        int cmp = UnsafeUtility.MemCmp(sourceBytes, destinationBytes, sourceSize);
                                        if (cmp != 0)
                                        {
                                            switch (pair.Key)
                                            {
                                                case "GameNodeStatusSystem":
                                                    switch (sourceName)
                                                    {
                                                        case "oldStatus":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(int*)sourceBytes, *(int*)destinationBytes);
                                                            break;
                                                        case "newStatus":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(int*)sourceBytes, *(int*)destinationBytes);
                                                            break;
                                                    }
                                                    break;
                                                case "GameDreamerSystem":
                                                    switch (sourceName)
                                                    {
                                                        case "dreamer":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(int*)sourceBytes, *(int*)destinationBytes);
                                                            break;
                                                        case "dreamerTime":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(double*)sourceBytes, *(double*)destinationBytes);
                                                            break;
                                                        case "delayTime":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(float*)sourceBytes, *(float*)destinationBytes);
                                                            break;
                                                    }
                                                    break;
                                                case "GameNodeSystem":
                                                    switch (sourceName)
                                                    {
                                                        case "status":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(int*)sourceBytes, *(int*)destinationBytes);
                                                            break;
                                                        case "speedScale":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(ushort*)sourceBytes, *(ushort*)destinationBytes);
                                                            break;
                                                        case "data":
                                                        case "newVelocity":
                                                        //Debug.LogError((*(Unity.Mathematics.half*)destinationBytes).value + ":" + (*(Unity.Mathematics.half*)sourceBytes).value + ":" + sourceSize);
                                                        //break;
                                                        case "oldVelocity":
                                                        case "oldAngle":
                                                        case "newAngle":
                                                        case "angle":
                                                        case "characterAngle":
                                                        case "deltaTime":
                                                        case "velocity":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(float*)sourceBytes, *(float*)destinationBytes);
                                                            break;
                                                        case "nextVelocity":
                                                        case "position":
                                                        case "translation":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(Unity.Mathematics.float3*)sourceBytes, *(Unity.Mathematics.float3*)destinationBytes);
                                                            break;
                                                        case "rotation":
                                                        case "oldRotation":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(Unity.Mathematics.quaternion*)sourceBytes, *(Unity.Mathematics.quaternion*)destinationBytes);
                                                            break;
                                                    }
                                                    break;
                                                case "GameNodeCharacterSystem":
                                                    switch (sourceName)
                                                    {
                                                        case "angle":
                                                        case "characterAngle":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(float*)sourceBytes, *(float*)destinationBytes);
                                                            break;
                                                        case "velocity":
                                                        case "newVelocity":
                                                        case "oldVelocity":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(Unity.Mathematics.float3*)sourceBytes, *(Unity.Mathematics.float3*)destinationBytes);
                                                            break;
                                                        case "oldRotation":
                                                        case "surfaceRotation":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(Unity.Mathematics.quaternion*)sourceBytes, *(Unity.Mathematics.quaternion*)destinationBytes);
                                                            break;
                                                    }
                                                    break;
                                                case "GameEntitySystem":
                                                    switch (sourceName)
                                                    {
                                                        case "angle":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(float*)sourceBytes, *(float*)destinationBytes);
                                                            break;
                                                    }
                                                    break;
                                                case "GameEntityActorSystem":
                                                    switch (sourceName)
                                                    {
                                                        case "distance":
                                                            UnityEngine.Assertions.Assert.AreEqual(*(Unity.Mathematics.float3*)sourceBytes, *(Unity.Mathematics.float3*)destinationBytes);
                                                            break;
                                                    }
                                                    break;
                                            }
                                        }

                                        UnityEngine.Assertions.Assert.IsTrue(
                                            cmp == 0,
                                            Dump(pair.Key, temp.name, sourceName, destinationName, key, __frameIndex));
                                    }
                                }

                                ++index;
                            }
                            
                            for (i = 0; i < numRenderCount; ++i)
                            {
                                ref Reader temp = ref readers[i];
                                if (temp.isReadable)
                                {
                                    while (temp.value.remainingItemCount > 0)
                                    {
                                        destinationName = temp.value.Read<FixedString32Bytes>().ToString();
                                        destinationSize = temp.value.Read<int>();
                                        destinationBytes = temp.value.Read(destinationSize);

                                        Debug.LogError(pair.Key + ":" + temp.name + ":" + destinationName);
                                    }

                                    temp.value.End();
                                }
                            }

                            reader.value.End();
                        }
                    }
                }

                /*foreach (var temp in pair.Value.Values)
                    __pool.Enqueue(temp.value);*/
            }

            UnityEngine.Assertions.Assert.raiseExceptions = isRaiseExceptions;
            //__streams = null;
        }

        public void Dispose()
        {
            foreach (var pair in __streams)
            {
                foreach (var temp in pair.Value.Values)
                {
                    temp.jobHandle.Complete();

                    __pool.Enqueue(temp.value);
                }
            }

            __streams = null;
        }
    }

    public uint minFrameIndex;
    public uint maxFrameCount = 128;

    private string __worldName;
    private SortedList<uint, Frame> __frames;
    private Queue<NativeHashStream<T>> __streams;

    public StreamScheduler Create(bool isClear, uint frameIndex, string systemName, string worldName)
    {
        UnityEngine.Assertions.Assert.AreNotEqual(World.Name, worldName);

        Frame frame = null;
        if (__frames == null)
        {
            __frames = new SortedList<uint, Frame>();

            if(isClear && this.minFrameIndex > 0)
                __worldName = worldName;
        }
        else
        {
            int count = __frames.Count;
            if (isClear && this.minFrameIndex > 0)
            {
                if (string.IsNullOrEmpty(__worldName))
                    __worldName = worldName;

                if (worldName != __worldName)
                {
                    uint minFrameIndex = Math.Min(this.minFrameIndex, frameIndex > maxFrameCount ? frameIndex - maxFrameCount : 0);
                    Frame temp;
                    while (count-- > 0)
                    {
                        if (__frames.Keys[0] < minFrameIndex)
                        {
                            temp = __frames.Values[0];
                            temp.Dump();
                            temp.Dispose();

                            __frames.RemoveAt(0);
                        }
                        else
                            break;
                    }
                }
            }

            int index = __frames.BinarySearch(frameIndex);

            var values = __frames.Values;
            if (index >= 0 && __frames.Keys[index] == frameIndex)
            {
                frame = values[index];
                frame.Remove(systemName, worldName);
            }

            for (int i = index + 1; i < count; ++i)
            {
                UnityEngine.Assertions.Assert.IsTrue(__frames.Keys[i] > frameIndex);
                values[i].Remove(worldName);
            }
        }

        if (frame == null)
        {
            if (__streams == null)
                __streams = new Queue<NativeHashStream<T>>();

            frame = new Frame(frameIndex, __streams);
            __frames[frameIndex] = frame;
        }

        return new StreamScheduler(systemName, worldName, frame);
    }

    public void Clear()
    {
        if (__frames != null)
        {
            foreach (var pair in __frames)
                pair.Value.Dispose();

            __frames.Clear();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if(__frames != null)
        {
            foreach(Frame frame in __frames.Values)
                frame.Dispose();

            __frames = null;
        }

        if(__streams != null)
        {
            foreach(var stream in __streams)
                stream.Dispose();

            __streams = null;
        }
    }

    protected override void OnUpdate()
    {
        throw new NotImplementedException();
    }
}

public partial class ComparisionValueSystem<TKey, TValue> : SystemBase
{
    private Dictionary<TKey, TValue> __items;

    private static ComparisionValueSystem<TKey, TValue> __instance;

    public static ComparisionValueSystem<TKey, TValue> instance
    {
        get
        {
            if (__instance == null)
                __instance = World.DefaultGameObjectInjectionWorld.CreateSystemManaged<ComparisionValueSystem<TKey, TValue>>();

            return __instance;
        }
    }

    public void Add(TKey key, TValue value, string message)
    {
        if (__items == null)
            __items = new Dictionary<TKey, TValue>();

        if (__items.TryGetValue(key, out var temp))
        {
            UnityEngine.Assertions.Assert.AreEqual(temp, value, message);
        }
        else
            __items[key] = value;
    }

    public void Remove(TKey key)
    {
        __items.Remove(key);
    }

    public void Clear()
    {
        if (__items != null)
            __items.Clear();
    }

    protected override void OnUpdate()
    {
        throw new NotImplementedException();
    }
}