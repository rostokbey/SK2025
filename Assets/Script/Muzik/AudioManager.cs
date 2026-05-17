using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Music Clips")]
    public AudioClip mainMenuMusic;
    public AudioClip battleMusic;

    [Header("Audio Source")]
    public AudioSource musicSource;

    private string currentMusicName = "";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource == null)
            musicSource = GetComponent<AudioSource>();

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
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.Play();

        currentMusicName = musicName;
    }
}