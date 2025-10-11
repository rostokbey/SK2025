using System.Linq;
using System.Collections.Generic;

public struct SeasonRow
{
    public int rank;
    public string name;
    public int points;
    public int wins;
    public int losses;
    public int medals;
    public int cups;
    public int stars;
    public bool isMe;
}

public static class SeasonAPI
{
    // Top-N + benim satýrým (liste içinde yoksa ayrýca döner)
    public static (List<SeasonRow> top, SeasonRow? me) GetStandings(int topN, string myProfileId)
    {
        var table = SeasonRepository.Load();
        var ordered = table.table
            .OrderByDescending(x => x.points)
            .ThenByDescending(x => x.wins)
            .ThenBy(x => x.losses)
            .ThenByDescending(x => x.consecutiveWins)
            .ToList();

        var top = new List<SeasonRow>(topN);
        SeasonRow? meRow = null;

        for (int i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var row = new SeasonRow
            {
                rank = i + 1,
                name = string.IsNullOrWhiteSpace(e.displayName) ? e.profileId : e.displayName,
                points = e.points,
                wins = e.wins,
                losses = e.losses,
                medals = e.medals,
                cups = e.cups,
                stars = e.stars,
                isMe = e.profileId == myProfileId,
            };

            if (i < topN) top.Add(row);
            if (row.isMe) meRow = row;
        }

        return (top, meRow);
    }

    // Konsola yazdýrmak için yardýmcý (UI baðlamadan test)
    public static void PrintStandingsToConsole(int topN, string myProfileId)
    {
        var (top, me) = GetStandings(topN, myProfileId);

        UnityEngine.Debug.Log("=== SEASON TOP ===");
        foreach (var r in top)
        {
            var tag = r.isMe ? " <- YOU" : "";
            UnityEngine.Debug.Log($"{r.rank}. {r.name}  P:{r.points}  W:{r.wins}/{r.losses}  M:{r.medals} K:{r.cups} Y:{r.stars}{tag}");
        }

        if (me.HasValue && me.Value.rank > topN)
        {
            var r = me.Value;
            UnityEngine.Debug.Log($"...  (SEN)  #{r.rank}  {r.name}  P:{r.points}  W:{r.wins}/{r.losses}  M:{r.medals} K:{r.cups} Y:{r.stars}");
        }
    }
}
