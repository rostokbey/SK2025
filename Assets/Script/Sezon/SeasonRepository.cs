using System.IO;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;


public static class SeasonRepository
{

    private static readonly string FileName = "season_table.json";
    private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static List<SeasonEntry> GetTop(int n)
    {
        var t = Load();
        return t.table
            .OrderByDescending(e => e.points)
            .ThenByDescending(e => e.wins)
            .ThenBy(e => e.losses)
            .Take(Mathf.Max(0, n))
            .ToList();
    }

    // Belirli profileId için sýralamadaki yeri (bulunamazsa -1)
    public static int GetRank(string profileId)
    {
        var t = Load();
        var ordered = t.table
            .OrderByDescending(e => e.points)
            .ThenByDescending(e => e.wins)
            .ThenBy(e => e.losses)
            .Select((e, idx) => new { e.profileId, Rank = idx + 1 })
            .ToList();

        var me = ordered.Find(x => x.profileId == profileId);
        return me != null ? me.Rank : -1;
    }
    public static SeasonTable Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new SeasonTable();
            var json = File.ReadAllText(FilePath);
            var t = JsonUtility.FromJson<SeasonTable>(json);
            return t ?? new SeasonTable();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[SeasonRepository] Load fail: " + ex.Message);
            return new SeasonTable();
        }
    }

    public static void Save(SeasonTable t)
    {
        try
        {
            var json = JsonUtility.ToJson(t, true);
            File.WriteAllText(FilePath, json);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[SeasonRepository] Save fail: " + ex.Message);
        }
    }

    
    public static SeasonEntry EnsureEntry(string profileId, string displayName)
    {
        var t = Load();
        var e = t.table.Find(x => x.profileId == profileId);
        if (e != null)
            return e;

        int seed = SeasonManager.Instance != null ? SeasonManager.Instance.StartPoints : 500;
        e.points = seed;
        e = new SeasonEntry
        {
            profileId = profileId,
            displayName = displayName,
            points = seed,               // <-- 500’den baþlat
            wins = 0,
            losses = 0,
            consecutiveWins = 0,
            consecutiveLosses = 0,
            medals = 0,
            cups = 0,
            stars = 0,
            eliminated = false
        };
        t.table.Add(e);
        Save(t);
        return e;
    }


    public static void Upsert(SeasonEntry entry)
    {
        var t = Load();
        var idx = t.table.FindIndex(x => x.profileId == entry.profileId);
        if (idx >= 0) t.table[idx] = entry; else t.table.Add(entry);
        Save(t);
    }

    // ---- Yardýmcýlar ----
    public static int Count() => Load().table.Count;

    public static SeasonEntry GetEntry(string profileId)
    {
        var t = Load();
        return t.table.Find(e => e.profileId == profileId);
    }

    public static void DumpToConsole()
    {
        try
        {
            var exists = File.Exists(FilePath);
            var json = exists ? File.ReadAllText(FilePath) : "(file not found)";
            Debug.Log($"[SeasonRepository] path: {FilePath}\n{json}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[SeasonRepository] Dump failed: " + ex.Message);
        }
    }
}
