using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Music Clips")]
    public AudioClip mainMenuMusic;
    public AudioClip battleMusic;

    [Header("Audio Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;

    private string currentMusicName = "";

    private void Awake()
    {
        // Singleton kontrolü
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Music Source kontrolü
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        // Hâlâ yoksa oluştur
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }

        // SFX Source oluştur
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }

        // Ayarlar
        musicSource.loop = true;
        musicSource.playOnAwake = false;

        sfxSource.playOnAwake = false;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        PlayMusicForScene(SceneManager.GetActiveScene().name);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayMusicForScene(scene.name);
    }

    private void PlayMusicForScene(string sceneName)
    {
        if (sceneName == "AnaSahne")
        {
            PlayMusic(mainMenuMusic, "MainMenu");
        }
        else if (sceneName == "BattleScene")
        {
            PlayMusic(battleMusic, "Battle");
        }
    }

    private void PlayMusic(AudioClip clip, string musicName)
    {
        if (clip == null) return;
        if (currentMusicName == musicName) return;

        musicSource.clip = clip;
        musicSource.Play();

        currentMusicName = musicName;
    }

    // =========================
    // SFX SYSTEM
    // =========================

    public void PlaySFX(string soundName)
    {
        if (string.IsNullOrEmpty(soundName))
            return;

        AudioClip clip = Resources.Load<AudioClip>("SFX/" + soundName);

        if (clip == null)
        {
            Debug.LogWarning("SFX bulunamadı: Resources/SFX/" + soundName);
            return;
        }

        sfxSource.PlayOneShot(clip);
    }
}