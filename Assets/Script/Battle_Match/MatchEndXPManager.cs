// Assets/Script/Battle_Match/MatchEndXPManager.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchEndXPManager : MonoBehaviour
{
    /// <summary>
    /// Maç sonunda XP dağıtımı ve sonuç verisi.
    /// oppUsed opsiyonel; yoksa göndermene gerek yok.
    /// </summary>
    public void GrantMatchRewards(
        bool myTeamWon,
        List<CardData> myUsed,
        List<CardData> oppUsed = null)   // <-- opsiyonel
    {
        if (myUsed == null || myUsed.Count == 0) return;

        var cls = CardLevelSystem.Instance;
        if (cls == null)
        {
            Debug.LogWarning("CardLevelSystem bulunamadı.");
            return;
        }

        // Delta: kazanma/kaybetme durumuna göre (senin CardLevelSystem'ındaki hesap)
        int delta = myTeamWon
            ? cls.ComputeWinXp(myUsed, oppUsed)     // pozitif
            : cls.ComputeLossXp(myUsed, oppUsed);   // negatif ya da düşük pozitif

        // Önceki değerleri topla (sonuç ekranı için)
        var before = new Dictionary<string, (int L, int XP)>();
        foreach (var c in myUsed)
            if (c != null) before[c.id] = (c.level, c.xp);

        // XP ver
        foreach (var c in myUsed)
            if (c != null) cls.AddExperience(c, delta);

        // Sonuç verisini doldur
        MatchResultBus.LastCards.Clear();
        foreach (var c in myUsed)
        {
            if (c == null) continue;
            var b = before[c.id];
            MatchResultBus.LastCards.Add(new CardXpDelta
            {
                card = c,
                oldLevel = b.L,
                newLevel = c.level,
                oldXp = b.XP,
                newXp = c.xp
            });
        }

        MatchResultBus.LastWasSeason = MatchContext.LastMatchWasSeason;
        MatchResultBus.LastWin = myTeamWon;

        // Sezon puanı (yalnızca sezon maçında)
        if (MatchContext.LastMatchWasSeason)
        {
            int teamSize = MatchContext.LastMatchTeamSize > 0 ? MatchContext.LastMatchTeamSize : 1;
            SeasonManager.Instance?.OnMatchFinished(myTeamWon, teamSize);
        }

        if (SceneManager.GetActiveScene().name == "BattleScene")
        {
            MatchResultUI.Show();
        }
    }
}