using UnityEngine;

public static class GameLanguage
{
    public const string NAME_SPACE = "Game Language";

    public const string CHINESE_SIMPLIFIED = "zh-CN";
    public const string CHINESE_TRADITIONAL = "zh-TW";
    public const string INDONESIAN = "in_ID";
    public const string ENGLISH = "en";

    public static string systemLanguage
    {
        get
        {
            string result;

            switch (Application.systemLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                    result = CHINESE_SIMPLIFIED; 
                    break;
                case SystemLanguage.ChineseTraditional:
                    result = CHINESE_TRADITIONAL;
                    break;
                case SystemLanguage.Indonesian:
                    result = INDONESIAN;
                    break;
                default:
                    result = ENGLISH;
                    break;
            }

            string language = GameConstantManager.Get("Language");

            return language != null && !language.Contains(result) ? ENGLISH : result;
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