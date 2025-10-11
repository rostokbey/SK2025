using System.Linq;
using System.Reflection;
using UnityEngine;

public static class SeasonSettlement
{
    static void Promote(SeasonEntry e)
    {
        while (e.medals >= 5) { e.medals -= 5; e.cups += 1; }
        while (e.cups >= 5) { e.cups -= 5; e.stars += 1; }
    }

    // Sezonu kapat: #1'e madalya, tüm kayıtları resetle
    public static void ForceSettle()
    {
        var table = SeasonRepository.Load();
        var ordered = table.table
            .OrderByDescending(x => x.points)
            .ThenByDescending(x => x.wins)
            .ThenBy(x => x.losses)
            .ThenByDescending(x => x.consecutiveWins)
            .ToList();

        if (ordered.Count == 0) { Debug.Log("[Season] ForceSettle: tablo boş."); return; }

        var champion = ordered[0];
        champion.medals += 1;
        Promote(champion);

        // Şampiyon kaydını masaya geri yaz
        var idx = table.table.FindIndex(x => x.profileId == champion.profileId);
        if (idx >= 0) table.table[idx] = champion;

        // Herkesi resetle
        // SeasonSettlement.cs  (ForceSettle() içi, reset bölümünün hemen başında)
        int seed = (SeasonManager.Instance != null) ? SeasonManager.Instance.StartPoints : 500;

        // ... herkesin puanını/serilerini resetlerken:
        foreach (var e in table.table)
        {
            e.points = seed;        // <-- 500 veya StartPoints
            e.wins = 0;
            e.losses = 0;
            e.consecutiveWins = 0;
            e.consecutiveLosses = 0;
            e.eliminated = false;
        }

        // kaydet & log
        SeasonRepository.Save(table);
        Debug.Log("[Season] Sezon kapandı; ödül verildi, puanlar resetlendi.");

        // --- ÖZET / POPUP ---
        var top = SeasonRepository.GetTop(1);
        var champEntry = top.Count > 0 ? top[0] : null;

        int participants = SeasonRepository.Count();

        SeasonBus.LastSettle = new SeasonSettleSummary
        {
            hadChampion = champEntry != null,
            championProfileId = champEntry != null ? champEntry.profileId : null,
            championName = champEntry != null ? champEntry.displayName : "(yok)",
            participants = participants,

            medalsDelta = champEntry != null ? 1 : 0,
            championMedalsAfter = champEntry != null ? champEntry.medals : 0,

            resetPoints = seed,                 // <-- yeniden tanımlamadık, mevcut seed'i kullandık
            note = "[Season] Settlement complete"
        };

        SeasonSettlementUI.Show();
    }

    }
