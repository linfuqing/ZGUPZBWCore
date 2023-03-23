using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using ZG;
using ZG.UI;

public class GameProgressbar : MonoBehaviour
{
    public enum ProgressbarType
    {
        Verify, 
        Unzip,
        Download,
        LoadScene,
        Other,
    }

    [Serializable]
    public struct ProgressbarInfo
    {
        //public GameObject unzip;
        //public GameObject download;

        //public GameObject unzipScenes;
        //public GameObject downloadScenes;

        //public GameObject loadScene;

        //public Progressbar progressbar;

        public Progressbar progressbar;

        public UnityEvent onEnable;
        public UnityEvent onDisable;

        public StringEvent onInfo;
    }

    private struct Instance
    {
        public int refCount;
        public HashSet<int> coroutineIndices;
    }

    public UnityEvent onError;

    public ProgressbarInfo[] progressbarInfos;

    private Instance[] __instances;

    private Pool<Coroutine> __coroutines;

    public static GameProgressbar instance
    {
        get;

        private set;
    }

    public bool isProgressing
    {
        get
        {
            return __instances[(int)ProgressbarType.LoadScene].refCount > 0 || __instances[(int)ProgressbarType.Other].refCount > 0;
        }
    }

    public int BeginCoroutine()
    {
        if (__coroutines == null)
            __coroutines = new Pool<Coroutine>();

        return __coroutines.Add(null);
    }

    public void EndCoroutine(int index, Coroutine coroutine)
    {
        if (__coroutines == null)
            __coroutines = new Pool<Coroutine>();

        __coroutines[index] = coroutine;
    }

    public int StartCoroutine(Coroutine coroutine)
    {
        if (__coroutines == null)
            __coroutines = new Pool<Coroutine>();

        return __coroutines.Add(coroutine);
    }

    public void ShowProgressBar(ProgressbarType type, int coroutineIndex = -1)
    {
        print($"Show Progress Bar : {type} : {coroutineIndex}");

        if (__instances == null)
            __instances = new Instance[progressbarInfos.Length];

        ref var instance = ref __instances[(int)type];

        if (coroutineIndex != -1)
        {
            if (instance.coroutineIndices == null)
                instance.coroutineIndices = new HashSet<int>();

            if(!instance.coroutineIndices.Add(coroutineIndex))
            {
                Debug.LogError("The Same Coroutine!");

                return;
            }
        }

        var progressbarInfo = progressbarInfos[(int)type];
        if (__instances[(int)type].refCount == 0)
        {
            if (progressbarInfo.onEnable != null)
                progressbarInfo.onEnable.Invoke();
        }

        if (progressbarInfo.onInfo != null)
            progressbarInfo.onInfo.Invoke(string.Empty);

        if (progressbarInfo.progressbar != null)
            progressbarInfo.progressbar.Reset(0.0f);

        ++instance.refCount;
    }

    public void UpdateProgressBar(ProgressbarType type, float progress, string info = "")
    {
        var progressbarInfo = progressbarInfos[(int)type];
        if (progressbarInfo.progressbar != null)
            progressbarInfo.progressbar.value = progress;

        if (progressbarInfo.onInfo != null)
            progressbarInfo.onInfo.Invoke(info);
    }

    public Coroutine ClearProgressBar(ProgressbarType type, int coroutineIndex = -1)
    {
        print($"Clear Progress Bar : {type} : {coroutineIndex}");

        ref var instance = ref __instances[(int)type];

        UnityEngine.Assertions.Assert.IsTrue(instance.refCount > 0);

        Coroutine coroutine = null;
        if (coroutineIndex != -1)
        {
            if (instance.coroutineIndices.Remove(coroutineIndex))
            {
                coroutine = __coroutines[coroutineIndex];

                __coroutines.RemoveAt(coroutineIndex);
            }
            else
                Debug.LogError("The Error Coroutine!");
        }

        if (--instance.refCount == 0)
        {
            UnityEngine.Assertions.Assert.AreEqual(0, instance.coroutineIndices == null ? 0 : instance.coroutineIndices.Count);

            var progressbarInfo = progressbarInfos[(int)type];

            if (progressbarInfo.onDisable != null)
                progressbarInfo.onDisable.Invoke();
        }

        return coroutine;
    }

    public void ShowProgressBar() => ShowProgressBar(ProgressbarType.Other, -1);

    public void UpdateProgressBar(float progress) => UpdateProgressBar(ProgressbarType.Other, progress);

    public void ClearProgressBar() => ClearProgressBar(ProgressbarType.Other, -1);

    public void ClearProgressBarAll(Action<Coroutine> handler)
    {
        Coroutine coroutine;
        int numInfos = progressbarInfos.Length;
        for (int i = 0; i < numInfos; ++i)
        {
            ref var instance = ref __instances[i];

            if(instance.coroutineIndices != null)
            {
                var coroutineIndices = new int[instance.coroutineIndices.Count];
                instance.coroutineIndices.CopyTo(coroutineIndices, 0);
                foreach (int coroutineIndex in coroutineIndices)
                {
                    coroutine = ClearProgressBar((ProgressbarType)i, coroutineIndex);

                    if (handler != null)
                        handler(coroutine);
                }
            }

            if (instance.refCount > 0)
            {
                instance.refCount = 1;

                ClearProgressBar((ProgressbarType)i, -1);
            }
        }
    }

    public void ClearProgressBarAll(Action<Coroutine> handler, bool isError)
    {
        ClearProgressBarAll(handler);

        if (isError)
            Error();
    }

    public void Error()
    {
        if (onError != null)
            onError.Invoke();
    }

    protected void OnEnable()
    {
        if (instance == null)
            instance = this;
    }

    protected void OnDisable()
    {
        if (instance == this)
            instance = null;
    }
}
