using System;
using System.Collections.Generic;

[Serializable]
public class SeasonEntry
{
    public string profileId;
    public string displayName;

    public int points = 500;
    public int wins;
    public int losses;

    public int consecutiveWins;
    public int consecutiveLosses;

    public int medals;   // 1.’ye ödül için
    public int cups;     // 5 madalya → 1 kupa
    public int stars;    // 5 kupa → 1 yıldız

    public bool eliminated; // elendi mi?
}

[Serializable]
public class SeasonTable
{
    public List<SeasonEntry> table = new List<SeasonEntry>();
}
