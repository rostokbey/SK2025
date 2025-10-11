using UnityEngine;

[DisallowMultipleComponent]
public class SeasonStandingsView : MonoBehaviour
{
    public static SeasonStandingsView Instance { get; private set; }

    [Header("Root (boþsa bu GO kullanýlýr)")]
    [SerializeField] private GameObject root;

    [SerializeField] private bool persistAcrossScenes = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
        if (root == null) root = gameObject;
    }

    void Start() { SetVisible(false); }

    public void SetVisible(bool on) { if (root) root.SetActive(on); }
    public bool IsVisible => (root ? root.activeSelf : gameObject.activeSelf);
}
