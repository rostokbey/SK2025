// Assets/Script/Sezon/SeasonManager.cs
using UnityEngine;
using System;

public class SeasonManager : MonoBehaviour
{
    public static SeasonManager Instance { get; private set; }

    [Header("Puan Kuralları")]
    [SerializeField] private int startPoints = 500;   
    public int StartPoints => startPoints;            
    [SerializeField] int winPoints = 50;
    [SerializeField] int lossPoints = 25;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
    }

    // Maç bittiğinde MatchEndXPManager burayı çağırıyor
    public void OnMatchFinished(bool myTeamWon, int teamSize)
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null) { Debug.LogWarning("[Season] PlayerInventory yok."); return; }

        string pid = inv.CurrentProfileId ?? "DEFAULT";

        // PlayerInventory'de PlayerName yoksa profileId kullan
        string displayName = pid;
        try
        {
            var pi = inv.GetType().GetProperty("PlayerName");
            if (pi != null && pi.PropertyType == typeof(string))
            {
                var v = (string)pi.GetValue(inv, null);
                if (!string.IsNullOrWhiteSpace(v)) displayName = v;
            }
        }
        catch { /* yoksay */ }

        var entry = SeasonRepository.EnsureEntry(pid, displayName);

        if (myTeamWon)
        {
            entry.points += winPoints;
            entry.wins++;
            entry.consecutiveWins++;
            entry.consecutiveLosses = 0;
        }
        else
        {
            int penalty = lossPoints;
            entry.losses++;
            entry.consecutiveLosses++;
            entry.consecutiveWins = 0;

            // 3. ardışık yenilgide 2x ceza
            if (entry.consecutiveLosses >= 3) penalty *= 2;

            entry.points = Mathf.Max(0, entry.points - penalty);
        }

        SeasonRepository.Upsert(entry);
    }

    // --- UI scriptlerinin beklediği yardımcılar ---
    public bool IsEliminated() => false;                 // şimdilik eleme yok
    public int GetMyPoints()
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        var e = SeasonRepository.GetEntry(inv?.CurrentProfileId ?? "DEFAULT");
        return e != null ? e.points : startPoints;
    }
    public int GetMyWinStreak()
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        var e = SeasonRepository.GetEntry(inv?.CurrentProfileId ?? "DEFAULT");
        return e != null ? e.consecutiveWins : 0;
    }
    public int GetMyLossStreak()
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        var e = SeasonRepository.GetEntry(inv?.CurrentProfileId ?? "DEFAULT");
        return e != null ? e.consecutiveLosses : 0;
    }

    // HUD TimeSpan beklediği için TimeSpan döndürelim
    public TimeSpan GetTimeLeft() => TimeSpan.Zero;

    // Sezon bitir/başlat
    public void StartNewSeason() => SeasonSettlement.ForceSettle();

    // UI Button üzerinden yanlışlıkla parametreli çağrılar için overload’lar:
    public void StartNewSeason(int _) => StartNewSeason();
    public void StartNewSeason(string _) => StartNewSeason();
}
