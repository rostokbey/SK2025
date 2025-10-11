using UnityEngine;

/// <summary>
/// Casual/Season bayraklarını saklar ve gerçek başlatmayı BattleStartAdapter’a devir eder.
/// </summary>
public class MatchStarter : MonoBehaviour
{
    public static MatchStarter Instance { get; private set; }

    [Header("Köprü")]
    public BattleStartAdapter adapter; // Inspector’dan bağla

    public static bool LastMatchWasSeason { get; private set; }
    public static int LastMatchTeamSize { get; private set; }

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
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

        LastMatchWasSeason = seasonMode;
        LastMatchTeamSize = Mathf.Clamp(teamSize, 1, 3);

        if (adapter == null)
        {
            Debug.LogError("[MatchStarter] adapter atanmadı.");
            return;
        }

        adapter.StartMatch(LastMatchTeamSize, LastMatchWasSeason);
        Debug.Log($"[MatchStarter] Start → teamSize={LastMatchTeamSize}, season={LastMatchWasSeason}");
    }
}
