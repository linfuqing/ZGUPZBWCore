using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using ZG;

public interface IGameAssetUnzipper
{
    IEnumerator Execute(AssetManager.DownloadHandler downloadHandler);
}

public class GameAssetManager : MonoBehaviour
{
    private struct Verifier
    {
        public string name
        {
            get;

            private set;
        }

        public int index
        {
            get;

            private set;
        }

        public int count
        {
            get;

            private set;
        }

        public static IEnumerator Start(AssetManager assetManager, string format)
        {
            var progressbar = GameProgressbar.instance;
            progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.Verify);

            Verifier verifier = default;

            using (var task = Task.Run(() => assetManager.Verify(verifier.__Change)))
            {
                do
                {
                    yield return null;

                    progressbar.UpdateProgressBar(GameProgressbar.ProgressbarType.Verify, verifier.index * 1.0f / verifier.count, verifier.ToString(format));

                } while (!task.IsCompleted);
            }

            progressbar.ClearProgressBar();
        }

        public string ToString(string format)
        {
            return string.Format(format, name, index, count);
        }

        private void __Change(string name, int index, int count)
        {
            this.name = name;
            this.index = index;
            this.count = count;
        }
    }

    private struct Tachometer
    {
        public const float DELTA_TIME = 1.0f;

        private float __value;
        private float __time;
        private ulong __totalBytesDownload;

        public float value => __value;

        public void Start()
        {
            __time = Time.time;
            __totalBytesDownload = 0;
        }

        public float Update(ulong totalBytesDownload)
        {
            if (totalBytesDownload > __totalBytesDownload)
            {
                float time = Time.unscaledTime, deltaTime = time - __time;
                if (deltaTime > DELTA_TIME)
                {
                    __value = (totalBytesDownload - __totalBytesDownload) / deltaTime;

                    __totalBytesDownload = totalBytesDownload;
                    __time = time;
                }
            }

            return __value;
        }
    }

    public struct AssetPath
    {
        public string value;
        public string filePrefix;
        public string urlPrefix;

        public string filePath
        {
            get
            {
                return string.IsNullOrEmpty(filePrefix) ? value : Path.Combine(filePrefix, value);
            }
        }

        public string url
        {
            get
            {
                string url = value.Replace('\\', '/');
                return string.IsNullOrEmpty(urlPrefix) ? url : $"{urlPrefix}/{url}";
            }
        }

        public AssetPath(string value, string filePrefix = null, string urlPrefix = null)
        {
            this.value = value;
            this.filePrefix = filePrefix;
            this.urlPrefix = urlPrefix;
        }
    }

    public event Action onConfirmCancel;

    public event Action onLoadAssetsStart;
    public event Action onLoadAssetsEnd;
    public event Action onLoadAssetsFinish;

    [UnityEngine.Serialization.FormerlySerializedAs("onComfirm")]
    public StringEvent onConfirm;

    public string verifyProgressFormat = "{1}/{2}";

    public string recompressProgressFormat = "{4:C2}/{5:C2}M({6}/{7})";//{0}:{1:P} 

    public string unzipProgressFormat = "{4:C2}/{5:C2}M({6}/{7})";//{0}:{1:P} 

    public string downloadProgressFormat = "{4:C2}/{5:C2}M({6}/{7}) {8:C2}M/S";//{0}:{1:P} 

    //public float timeout;

    private bool __isMissingConfirm;
    private bool __isConfirm;

    private int __sceneCoroutineIndex = -1;
    private Coroutine __assetCoroutine;

    private Tachometer __tachometer;

    private AssetManager __assetManager;

    public static GameAssetManager instance
    {
        get;

        private set;
    }

    public float speed => __tachometer.value;

    public string sceneName
    {
        get;

        private set;
    }

    public string nextSceneName
    {
        get;

        private set;
    }

    public AssetManager dataManager
    {
        get
        {
            return __assetManager;
        }
    }

    public AssetManager sceneManager
    {
        get
        {
            return __assetManager;
        }
    }

    public static string GetStreamingAssetsURL(string path)
    {
        if (string.IsNullOrEmpty(path))
            return __GetStreamingAssetsURL(Application.streamingAssetsPath);

        return __GetStreamingAssetsURL($"{Application.streamingAssetsPath}/{path}");
    }

    [Preserve]
    public void ConfirmOk()
    {
        __isConfirm = true;
    }

    [Preserve]
    public void ConfirmCancel()
    {
        if (onConfirmCancel != null)
            onConfirmCancel();
    }

    public void ClearProgressBarAll(bool isError)
    {
        GameProgressbar.instance.ClearProgressBarAll(StopCoroutine, isError);

        __sceneCoroutineIndex = -1;

        if (__assetCoroutine != null)
        {
            StopCoroutine(__assetCoroutine);

            __assetCoroutine = null;
        }
    }

    public IEnumerator Init(string defaultSceneName, string scenePath, string path, string url)
    {
        var progressBar = GameProgressbar.instance;
        progressBar.ShowProgressBar(GameProgressbar.ProgressbarType.Other);

        string language = GameLanguage.overrideLanguage;

        string persistentDataPath = Path.Combine(Application.persistentDataPath, language);
        __assetManager = new AssetManager(Path.Combine(persistentDataPath, path));

        string assetURL = url == null ? null : $"{url}/{Application.platform}/{language}";
        //yield return __LoadAssets(resourcesURL, path, scenePath);

        yield return __LoadAssets(assetURL, new AssetPath[] { new AssetPath(scenePath, language) }, null);

        //__isMissingConfirm = false;

        //world = WorldUtility.GetOrCreateWorld("Client");

        nextSceneName = defaultSceneName;

        yield return __LoadScene(-1, null);

        progressBar.ClearProgressBar(GameProgressbar.ProgressbarType.Other);
    }

    public bool StopLoadingScene()
    {
        if (__sceneCoroutineIndex == -1)
            return false;

        var coroutine = GameProgressbar.instance.ClearProgressBar(GameProgressbar.ProgressbarType.LoadScene, __sceneCoroutineIndex);

        StopCoroutine(coroutine);

        __sceneCoroutineIndex = -1;

        return true;
    }

    public void LoadScene(string name, Action onComplete)
    {
        StopLoadingScene();

        nextSceneName = name;

        if (__sceneCoroutineIndex == -1)
        {
            var progressbar = GameProgressbar.instance;

            __sceneCoroutineIndex = progressbar.BeginCoroutine();

            var coroutine = StartCoroutine(__LoadScene(__sceneCoroutineIndex, onComplete));

            progressbar.EndCoroutine(__sceneCoroutineIndex, coroutine);
        }
    }

    public void LoadAssets(
        bool isVerified, 
        Action onComplete, 
        string url, 
        AssetPath[] paths, 
        IGameAssetUnzipper[] unzippers)
    {
        var progressbar = GameProgressbar.instance;

        __assetCoroutine = StartCoroutine(__LoadAssets(
            isVerified, 
            __assetCoroutine, 
            onComplete, 
            url, 
            paths, 
            unzippers));
    }

    private IEnumerator __LoadAssets(string url, AssetPath[] paths, IGameAssetUnzipper[] unzippers)
    {
        var progressbar = GameProgressbar.instance;

        (IAssetPack, ulong)[] assetPacks = null;
        IAssetPack assetPack;
        IAssetPackHeader assetPackHeader;
        ulong fileSize, size = 0;
        int i, j, length = paths.Length;
        for (i = 0; i < length; ++i)
        {
            assetPack = AssetUtility.RetrievePack(paths[i].filePath);

            if (assetPacks == null)
                assetPacks = new (IAssetPack, ulong)[length];
            else
            {
                for(j = 0; j < i; ++j)
                {
                    if(assetPacks[j].Item1 == assetPack)
                        break;
                }

                if (j < i)
                {
                    assetPacks[i] = (assetPack, 0);

                    continue;
                }
            }

            assetPackHeader = assetPack == null ? null : assetPack.header;
            if (assetPackHeader == null)
                continue;

            while (!assetPackHeader.isDone)
                yield return null;

            fileSize = assetPackHeader.fileSize;

            size += (ulong)Math.Round(fileSize * (double)(1.0f - assetPack.downloadProgress));

            assetPacks[i] = (assetPack, fileSize);
        }

        if(size > 0)
        {
            progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.Download);

            (IAssetPack, ulong) assetPackAndFileSize;
            ulong bytesNeedToDownload = 0L;
            uint downloadedBytes;
            bool isDone;
            do
            {
                yield return null;

                isDone = true;
                for (i = 0; i < length; ++i)
                {
                    assetPackAndFileSize = assetPacks[i];
                    assetPack = assetPackAndFileSize.Item1;
                    if (assetPack == null)
                        continue;

                    bytesNeedToDownload += (ulong)Math.Round(assetPackAndFileSize.Item2 * (double)(1.0f - assetPack.downloadProgress));

                    isDone &= assetPack.isDone;
                }

                downloadedBytes = (uint)(size - bytesNeedToDownload);

                __Download("Packs", downloadedBytes * 1.0f / size, downloadedBytes, downloadedBytes, size, 0, 1);

            } while (!isDone);

            progressbar.ClearProgressBar(GameProgressbar.ProgressbarType.Download);
        }

        progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.Unzip);

        if(unzippers != null)
        {
            foreach(var unzipper in unzippers)
                yield return unzipper.Execute(__Unzip);
        }

        string streamingAssetsPath = Application.streamingAssetsPath, folder;
        AssetPath path;
        var assetPaths = new ZG.AssetPath[length];
        for (i = 0; i < length; ++i)
        {
            path = paths[i];
            folder = Path.GetDirectoryName(path.value);

            if (!string.IsNullOrEmpty(folder))
                __assetManager.LoadFrom(path.value);

            assetPaths[i] = new ZG.AssetPath(GetStreamingAssetsURL(path.filePath), folder, assetPacks == null ? null : assetPacks[i].Item1);
        }

        __tachometer.Start();

        yield return __assetManager.GetOrDownload(
            null,
            __Unzip,
            assetPaths);

        progressbar.ClearProgressBar(GameProgressbar.ProgressbarType.Unzip);

        /*if (progressbarInfo.onInfo != null)
            progressbarInfo.onInfo.Invoke(string.Empty);*/

        if (!string.IsNullOrEmpty(url))
        {
            progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.Download);

            /*if (!string.IsNullOrEmpty(suffix))
                url = $"{url}/{suffix}";*/

            for (i = 0; i < length; ++i)
            {
                path = paths[i];

                assetPaths[i] = new ZG.AssetPath($"{url}/{path.url}", assetPaths[i].folder, null);
            }

            __tachometer.Start();

            yield return __assetManager.GetOrDownload(
                __Confirm,
                __Download,
                assetPaths);

            progressbar.ClearProgressBar(GameProgressbar.ProgressbarType.Download);

            /*if (progressbarInfo.onInfo != null)
                progressbarInfo.onInfo.Invoke(string.Empty);*/
        }
    }

    private IEnumerator __LoadAssets(
        bool isVerified, 
        Coroutine coroutine, 
        Action onComplete, 
        string url, 
        AssetPath[] paths,
        IGameAssetUnzipper[] unzippers)
    {
        if (coroutine != null)
            yield return coroutine;

        if (isVerified)
            yield return Verifier.Start(__assetManager, verifyProgressFormat);
        else if (onLoadAssetsStart != null)
            onLoadAssetsStart();

        yield return __LoadAssets(url, paths, unzippers);

        if (!isVerified)
        {
            if (onLoadAssetsEnd != null)
                onLoadAssetsEnd();
        }

        __isMissingConfirm = false;

        if (onComplete != null)
            onComplete();

        var progressbar = GameProgressbar.instance;
        progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.Verify);

        __tachometer.Start();

        yield return __assetManager.Recompress(__Recompress);

        progressbar.ClearProgressBar(GameProgressbar.ProgressbarType.Verify);

        if (!isVerified)
        {
            if (onLoadAssetsFinish != null)
                onLoadAssetsFinish();
        }

        __assetCoroutine = null;
    }

    private IEnumerator __LoadScene(int coroutineIndex, Action onComplete)
    {
        var progressbar = GameProgressbar.instance;

        /*if (progressbarInfo.progressbar != null)
            progressbarInfo.progressbar.Reset(0.0f);

        if (progressbarInfo.loadScene != null)
            progressbarInfo.loadScene.SetActive(true);*/

        progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.LoadScene, coroutineIndex);

        //等待断开连接的对象调用OnDestroy
        yield return null;

        while (this.nextSceneName != null)
        {
            string sceneName = this.sceneName;
            if (!string.IsNullOrEmpty(sceneName))
            {
                var scene = SceneManager.GetSceneByName(Path.GetFileNameWithoutExtension(sceneName));
                var gameObjects = scene.IsValid() ? scene.GetRootGameObjects() : null;
                if(gameObjects != null)
                {
                    foreach (var gameObject in gameObjects)
                        Destroy(gameObject);
                }
                //yield return SceneManager.UnloadSceneAsync(Path.GetFileNameWithoutExtension(sceneName), UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);

                //等待场景对象调用OnDestroy
                yield return null;

                if (__assetManager != null)
                    __assetManager.UnloadAssetBundle(sceneName);
            }

            string nextSceneName = this.nextSceneName;

            this.sceneName = nextSceneName;

            AssetBundle assetBundle = null;
            if (__assetManager != null)
                yield return __assetManager.LoadAssetBundleAsync(nextSceneName, __LoadingScene, x => assetBundle = x);

            var loader = SceneManager.LoadSceneAsync(Path.GetFileNameWithoutExtension(nextSceneName), LoadSceneMode.Single);
            if (loader != null)
            {
                while (!loader.isDone)
                {
                    progressbar.UpdateProgressBar(GameProgressbar.ProgressbarType.LoadScene, loader.progress + 0.1f);

                    yield return null;
                }
            }

            /*if (assetBundle != null)
                assetBundle.Unload(false);*/

            //Caching.ClearCache();

            yield return Resources.UnloadUnusedAssets();

            if (nextSceneName == this.nextSceneName)
                this.nextSceneName = null;
        }

        progressbar.ClearProgressBar(GameProgressbar.ProgressbarType.LoadScene, coroutineIndex);

        if (coroutineIndex == __sceneCoroutineIndex)
            __sceneCoroutineIndex = -1;

        if (onComplete != null)
            onComplete();

        /*if (progressbarInfo.loadScene != null)
            progressbarInfo.loadScene.SetActive(false);*/
    }

    /*private IEnumerator __Load(string url, string folder)
    {
        if (progressbarInfo.unzip != null)
            progressbarInfo.unzip.SetActive(true);

        string streamingAssetsPath = Application.streamingAssetsPath;

        if (__assetManager == null)
        {
            string persistentDataPath = Application.persistentDataPath;
            __assetManager = new AssetManager(string.IsNullOrEmpty(path) ? persistentDataPath : Path.Combine(persistentDataPath, path));

            if (string.IsNullOrEmpty(folder) ? __assetManager.assetCount < 1 : !__assetManager.Contains(folder))
                yield return __assetManager.LoadAll(
                    string.IsNullOrEmpty(path) ? __GetPath(streamingAssetsPath) : __GetPath(Path.Combine(streamingAssetsPath, path)),
                    folder,
                    __Download,
                    0.0f);
        }
        else if (!string.IsNullOrEmpty(folder))
        {
            streamingAssetsPath = Path.Combine(streamingAssetsPath, folder);

            string name = Path.GetFileName(folder);

            __assetManager.LoadFrom(Path.Combine(folder, name));
            if (!__assetManager.Contains(folder))
                yield return __assetManager.LoadAll(
                    __GetPath(Path.Combine(streamingAssetsPath, name)),
                    folder,
                    __Download,
                    0.0f);
        }

        if (progressbarInfo.text != null)
            progressbarInfo.text.text = string.Empty;

        if (progressbarInfo.unzip != null)
            progressbarInfo.unzip.SetActive(false);

        if (!string.IsNullOrEmpty(url))
        {
            if (progressbarInfo.download != null)
                progressbarInfo.download.SetActive(true);

            yield return __assetManager.LoadAll(
                url + '/' + path,
                folder,
                __Download,
                timeout);

            if (progressbarInfo.text != null)
                progressbarInfo.text.text = string.Empty;

            if (progressbarInfo.download != null)
                progressbarInfo.download.SetActive(false);
        }

        if (!string.IsNullOrEmpty(scenePath))
        {
            if (progressbarInfo.unzipScene != null)
                progressbarInfo.unzipScene.SetActive(true);

            string path = string.IsNullOrEmpty(folder) ? scenePath : Path.Combine(folder, scenePath);

            folder = Path.GetDirectoryName(path);

            __assetManager.LoadFrom(path);
            if (!__assetManager.Contains(folder))
                yield return __assetManager.LoadAll(
                    __GetPath(Path.Combine(streamingAssetsPath, scenePath)),
                    folder,
                    __Download,
                    0.0f);

            if (progressbarInfo.text != null)
                progressbarInfo.text.text = string.Empty;

            if (progressbarInfo.unzipScene != null)
                progressbarInfo.unzipScene.SetActive(false);

            if (!string.IsNullOrEmpty(url))
            {
                if (progressbarInfo.downloadScene != null)
                    progressbarInfo.downloadScene.SetActive(true);

                yield return __assetManager.LoadAll(
                    url + '/' + scenePath,
                    folder,
                    __Download,
                    timeout);

                if (progressbarInfo.text != null)
                    progressbarInfo.text.text = string.Empty;

                if (progressbarInfo.downloadScene != null)
                    progressbarInfo.downloadScene.SetActive(false);
            }
        }
    }*/

    private void __LoadingScene(float progress)
    {
        GameProgressbar.instance.UpdateProgressBar(GameProgressbar.ProgressbarType.LoadScene, progress * 0.1f);

        /*if (progressbarInfo.progressbar != null)
            progressbarInfo.progressbar.value = progress * 0.1f;*/
    }

    private void __Recompress(
        string name,
        float progress,
        uint bytesDownload,
        ulong totalBytesDownload,
        ulong totalBytes,
        int index,
        int count)
    {
        GameProgressbar.instance.UpdateProgressBar(
            GameProgressbar.ProgressbarType.Verify,
            //(index + progress) / count,
            (float)(totalBytesDownload * 1.0 / totalBytes),
            __GetProgressInfo(
                recompressProgressFormat,
                name,
                progress,
                bytesDownload,
                totalBytesDownload,
                totalBytes,
                index,
                count));
    }

    private void __Unzip(
        string name, 
        float progress, 
        uint bytesDownload, 
        ulong totalBytesDownload, 
        ulong totalBytes, 
        int index, 
        int count)
    {
        GameProgressbar.instance.UpdateProgressBar(
            GameProgressbar.ProgressbarType.Unzip,
               //(index + progress) / count,
            (float)(totalBytesDownload * 1.0 / totalBytes),
            __GetProgressInfo(
                unzipProgressFormat, 
                name, 
                progress, 
                bytesDownload, 
                totalBytesDownload, 
                totalBytes, 
                index, 
                count));
    }

    private void __Download(string name, float progress, uint bytesDownload, ulong totalBytesDownload, ulong totalBytes, int index, int count)
    {
        GameProgressbar.instance.UpdateProgressBar(
               GameProgressbar.ProgressbarType.Download,
               //(index + progress) / count,
               (float)(totalBytesDownload * 1.0 / totalBytes),
               __GetProgressInfo(
                   downloadProgressFormat, 
                   name, 
                   progress, 
                   bytesDownload, 
                   totalBytesDownload, 
                   totalBytes, 
                   index, 
                   count));
    }

    private IEnumerator __Confirm(ulong size)
    {
        if (__isMissingConfirm)
        {
            __isConfirm = true;

            yield break;
        }

        __isMissingConfirm = true;

        __isConfirm = false;

        if (onConfirm != null)
            onConfirm.Invoke((size / (1024.0 * 1024.0)).ToString("F2"));

        while (!__isConfirm)
            yield return null;
    }

    private string __GetProgressInfo(
        string format, 
        string name, 
        float progress, 
        uint bytesDownload, 
        ulong totalBytesDownload, 
        ulong totalBytes, 
        int index, 
        int count)
    {
        float m = 1024.0f * 1024.0f;

        return string.Format(
            format,
            name, 
            progress,
            bytesDownload,
            bytesDownload / progress, 
            totalBytesDownload * 1.0 / m,
            totalBytes * 1.0 / m,
            index,
            count,
            __tachometer.Update(totalBytesDownload) / m);
    }

    private static string __GetStreamingAssetsURL(string path)
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsServer:
                return "file:///" + path;
            case RuntimePlatform.OSXEditor:
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.IPhonePlayer:
            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxServer:
            case RuntimePlatform.LinuxEditor:
                return "file://" + path;
        }

        return path;
    }

    void OnEnable()
    {
        instance = this;
    }

    void OnDisable()
    {
        instance = null;
    }
}
