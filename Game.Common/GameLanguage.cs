using UnityEngine;

public static class GameLanguage
{
    public const string NAME_SPACE = "Game Language";

    public const string CHINESE_SIMPLIFIED = "zh-CN";
    public const string CHINESE_TRADITIONAL = "zh-TW";
    public const string ENGLISH = "en";

    public static string systemLanguage
    {
        get
        {
            string language = GameConstantManager.Get("Language");
            if (language != null)
                return language;

            switch (Application.systemLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                    return CHINESE_SIMPLIFIED;
                case SystemLanguage.ChineseTraditional:
                    return CHINESE_TRADITIONAL;
            }

            return ENGLISH;
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