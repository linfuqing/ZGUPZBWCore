using UnityEngine;

public static class GameLanguage
{
    public const string NAME_SPACE = "Game Language";

    public const string CHINESE_SIMPLIFIED = "zh-CN";
    public const string CHINESE_TRADITIONAL = "zh-TW";
    public const string INDONESIAN = "in-ID";
    public const string ENGLISH = "en";

    public static string systemLanguage
    {
        get
        {
            string result, language = GameConstantManager.Get("Language") ?? ENGLISH;
             
            switch (Application.systemLanguage)
            {
                case SystemLanguage.English:
                    result = ENGLISH;
                    break;
                case SystemLanguage.ChineseSimplified:
                    result = CHINESE_SIMPLIFIED; 
                    break;
                case SystemLanguage.ChineseTraditional:
                case SystemLanguage.Chinese:
                    result = CHINESE_TRADITIONAL;
                    break;
                case SystemLanguage.Indonesian:
                    result = INDONESIAN;
                    break;
                default:
                    result = language;
                    break;
            }

            string languages = GameConstantManager.Get("Languages");

            return languages != null &&  languages.Contains(result) ? result : language;
        }
    }

    public static string overrideLanguage
    {
        get
        {
            var result = PlayerPrefs.GetString(NAME_SPACE);

            return string.IsNullOrEmpty(result) ? systemLanguage : result;
        }

        set
        {
            PlayerPrefs.SetString(NAME_SPACE, value);
        }
    }

}