using System.Collections.Generic;
using UnityEngine;

public static class MatchEndXpPatch
{
    /// <summary>
    /// XP’ler daðýtýldýktan sonra çaðýr. Season maçýysa SeasonManager puanýný tek kez iþler.
    /// </summary>
    public static void AfterXpApplied(bool myTeamWon, IList<CardData> myUsed)
    {
        // (Ýstersen burada result snapshot’ý hazýrlayýp MatchResultBus’a yazabilirsin)
        if (MatchContext.LastMatchWasSeason)
        {
            int teamSize = MatchContext.LastMatchTeamSize > 0 ? MatchContext.LastMatchTeamSize : 1;
            SeasonManager.Instance?.OnMatchFinished(myTeamWon, teamSize);
        }

        Debug.Log($"[MatchEndXpPatch] Done. Season={MatchContext.LastMatchWasSeason}, TeamSize={MatchContext.LastMatchTeamSize}");
    }
}

