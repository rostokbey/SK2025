using UnityEngine;

/// <summary>
/// Casual/Season maçını host olarak başlatır. Maç bağlamını (sezon/casual, takım boyutu)
/// MatchContext'e yazar; maç sonu XP ve sezon puanı oradan okunur.
/// </summary>
public class MatchStarter : MonoBehaviour
{
    public static MatchStarter Instance { get; private set; }

    [Header("Köprü")]
    public BattleStartAdapter adapter; // Inspector’dan bağla

    public LobbyUIManager lobbyUIManager;
    public LobbyTeamManager lobbyTeamManager;

    // Eskiden bunlar ayrı bir kopyaydı ve MatchEndXPManager'ın okuduğu MatchContext'e HİÇ yazılmıyordu
    // (sezon puanı işlenmiyordu). Artık tek kaynak MatchContext; eski çağıranlar için yönlendirme.
    public static bool LastMatchWasSeason => MatchContext.LastMatchWasSeason;
    public static int LastMatchTeamSize => MatchContext.LastMatchTeamSize;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <param name="teamSize">1 / 2 / 3</param>
    /// <param name="seasonMode">true: sezon maçı, false: casual</param>
    public void StartMatch(int teamSize, bool seasonMode)
    {
        if (seasonMode && SeasonManager.Instance?.IsEliminated() == true)
        {
            Debug.LogWarning("Bu sezon elendiniz (puan=0).");
            return;
        }

        int size = Mathf.Clamp(teamSize, 1, 3);

        LobbyTeamManager.ResetMatchSnapshot();

        if (lobbyTeamManager == null)
            lobbyTeamManager = LobbyTeamManager.Instance
                ?? Object.FindFirstObjectByType<LobbyTeamManager>(FindObjectsInactive.Include);

        if (lobbyTeamManager != null)
        {
            lobbyTeamManager.SetMaxPlayersPerTeam(size);
        }
        else
        {
            Debug.LogError("[MatchStarter] LobbyTeamManager bulunamadı; mod başlatılmadı.");
            return;
        }

        if (lobbyUIManager == null)
            lobbyUIManager = Object.FindFirstObjectByType<LobbyUIManager>(FindObjectsInactive.Include);

        if (lobbyUIManager == null)
        {
            Debug.LogError("[MatchStarter] LobbyUIManager atanmadı.");
            return;
        }

        // Bağlam yalnızca maç gerçekten başlatılacaksa yazılır (hatalı denemeler bayrağı bozmasın).
        MatchContext.Set(seasonMode, size);
        lobbyUIManager.HostGame();

        Debug.Log($"[MatchStarter] Start → teamSize={MatchContext.LastMatchTeamSize}, season={MatchContext.LastMatchWasSeason}");
    }

    // ===== CASUAL BUTTONS =====

    public void StartCasual1v1()
    {
        StartMatch(1, false);
    }

    public void StartCasual2v2()
    {
        StartMatch(2, false);
    }

    public void StartCasual3v3()
    {
        StartMatch(3, false);
    }
}