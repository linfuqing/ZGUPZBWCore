using System;
using System.Collections.Generic;
using UnityEngine;
using ZG;

[EntityComponent(typeof(GameActionFixedFrame))]
[EntityComponent(typeof(GameActionFixedNextFrame))]
[EntityComponent(typeof(GameActionFixedStage))]
[EntityComponent(typeof(GameActionFixedStageIndex))]
public class GameActionFixedComponent : EntityProxyComponent, IEntityComponent
{
    [Serializable]
    public struct NextFrame
    {
        [Index("frames", pathLevel = -1)]
        public int frameIndex;
        public float chance;
    }

    [Serializable]
    public struct Frame
    {
#if UNITY_EDITOR
        public string name;
#endif
        public string animationTrigger;
        //public int actionIndex;
        public float speedScale;
        public float range;
        public float minTime;
        public float maxTime;
        public Vector3 position;
        public Vector3 rotation;

        public NextFrame[] nextFrames;
    }

    [Serializable]
    public struct Stage
    {
#if UNITY_EDITOR
        public string name;
#endif
        public NextFrame[] nextFrames;
    }

    public Frame[] frames;
    public Stage[] stages;

    private static List<GameActionFixedNextFrame> __nextFrames = new List<GameActionFixedNextFrame>();
    private static List<GameActionFixedFrame> __frames = new List<GameActionFixedFrame>();
    private static List<GameActionFixedStage> __stages = new List<GameActionFixedStage>();

    public int stageIndex
    {
        get => this.GetComponentData<GameActionFixedStageIndex>().value;

        set
        {
            GameActionFixedStageIndex stageIndex;
            stageIndex.value = value;
            this.SetComponentData(stageIndex);
        }
    }

    void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
    {
        __nextFrames.Clear();
        __frames.Clear();
        __stages.Clear();

        int frameIndex = 0;
        GameActionFixedNextFrame targetNextFrame;

        GameActionFixedFrame targetFrame;
        foreach (var frame in frames)
        {
            targetFrame.animationTrigger = frame.animationTrigger;
            targetFrame.speedScale = (Unity.Mathematics.half)frame.speedScale;
            targetFrame.rangeSq = frame.range * frame.range;
            targetFrame.minTime = frame.minTime;
            targetFrame.maxTime = frame.maxTime;
            targetFrame.position = frame.position;
            targetFrame.rotation = Quaternion.Euler(frame.rotation);

            targetFrame.nextFrameStartIndex = frameIndex;
            targetFrame.nextFrameCount = frame.nextFrames.Length;

            frameIndex += targetFrame.nextFrameCount;

            foreach (var nextFrame in frame.nextFrames)
            {
                targetNextFrame.frameIndex = nextFrame.frameIndex;
                targetNextFrame.chance = nextFrame.chance;

                __nextFrames.Add(targetNextFrame);
            }

            __frames.Add(targetFrame);
        }

        GameActionFixedStage targetStage;
        foreach (var stage in stages)
        {
            targetStage.nextFrameStartIndex = frameIndex;
            targetStage.nextFrameCount = stage.nextFrames.Length;

            frameIndex += targetStage.nextFrameCount;

            foreach (var nextFrame in stage.nextFrames)
            {
                targetNextFrame.frameIndex = nextFrame.frameIndex;
                targetNextFrame.chance = nextFrame.chance;

                __nextFrames.Add(targetNextFrame);
            }

            __stages.Add(targetStage);
        }

        assigner.SetBuffer<GameActionFixedNextFrame, List<GameActionFixedNextFrame>>(EntityComponentAssigner.BufferOption.Override, entity, __nextFrames);
        assigner.SetBuffer<GameActionFixedFrame, List<GameActionFixedFrame>>(EntityComponentAssigner.BufferOption.Override, entity, __frames);
        assigner.SetBuffer<GameActionFixedStage, List<GameActionFixedStage>>(EntityComponentAssigner.BufferOption.Override, entity, __stages);
    }
}
