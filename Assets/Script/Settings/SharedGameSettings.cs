using UnityEngine;

/// <summary>Ana menü ve savaş sahnesinin ortak kalıcı ayarları.</summary>
public static class SharedGameSettings
{
    private const string SoundKey = "settings_sound";
    private const string MusicKey = "settings_music";
    private const string VibrationKey = "settings_vibration";

    public static bool SoundEnabled
    {
        get => PlayerPrefs.GetInt(SoundKey, 1) == 1;
        set { PlayerPrefs.SetInt(SoundKey, value ? 1 : 0); SaveAndApply(); }
    }

    public static bool MusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicKey, 1) == 1;
        set { PlayerPrefs.SetInt(MusicKey, value ? 1 : 0); SaveAndApply(); }
    }

    public static bool VibrationEnabled
    {
        get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
        set { PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static void Apply()
    {
        if (AudioManager.Instance != null)
        {
            if (AudioManager.Instance.musicSource != null)
                AudioManager.Instance.musicSource.mute = !MusicEnabled;
            if (AudioManager.Instance.sfxSource != null)
                AudioManager.Instance.sfxSource.mute = !SoundEnabled;
        }
    }

    private static void SaveAndApply()
    {
        PlayerPrefs.Save();
        Apply();
    }
}
