using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine.Networking;
using System.Diagnostics;
using System.IO;

public class GameConstantManager : UnityEngine.MonoBehaviour
{
    public const string KEY_MARKET = "Market";
    public const string KEY_CHANNEL = "Channel";
    public const string KEY_CHANNEL_API = "ChannelAPI";
    public const string KEY_CDN_URL = "CDNURL";

    public string[] paths =
    {
        "Config.txt", 
        "URLs.txt"
    };

    private static int? __count;
    private static Dictionary<string, string> __args;

    public static bool isInit => __count != null && __count.Value < 1;

    public static IReadOnlyDictionary<string, string> args => __args;

    public static string Get(string key)
    {
        if (__args.TryGetValue(key, out string value))
            return value;

        return null;
    }

    public static string Get(Type type)
    {
        return Get(type.Name);
    }

    public static void Init(string[] args, int startIndex = 0)
    {
        var regex = new Regex("\"(\\w+)\"\\s*=\\s*\"(.+)\"");
        Match match;
        int numArgs = args.Length;
        for (int i = startIndex; i < numArgs; ++i)
        {
            match = regex.Match(args[i]);
            if (match == null || !match.Success)
                continue;

            if (__args == null)
                __args = new Dictionary<string, string>();

            __args[match.Result("$1")] = match.Result("$2");
        }
    }

    void Awake()
    {
        if (__count == null)
            __count = 1;
        else
            __count = __count.Value + 1;
    }

    IEnumerator Start()
    {
        string url;
        StringBuilder stringBuilder = null;
        foreach (var path in paths)
        {
            url = GameAssetManager.GetStreamingAssetsURL(path);
            using (var www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    if (stringBuilder == null)
                        stringBuilder = new StringBuilder(www.downloadHandler.text);
                    else
                    {
                        stringBuilder.Append('\n');
                        stringBuilder.Append(www.downloadHandler.text);
                    }
                }
                else
                    UnityEngine.Debug.LogError($"{www.error} : {url}");
            }
        }

        Init(stringBuilder.ToString().Split('\n'));

        __count = __count.Value - 1;
    }
}
