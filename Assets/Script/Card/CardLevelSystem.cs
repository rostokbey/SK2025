using System.Collections.Generic;
using UnityEngine;

public class CardLevelSystem : MonoBehaviour
{
    public static CardLevelSystem Instance { get; private set; }

    [Header("XP / Seviye Ayarları")]
    [SerializeField] private int maxLevel = 99;
    [SerializeField] private int baseWinXp = 50;   // kazanma baz XP
    [SerializeField] private int baseLossXp = 20;  // kaybetme baz XP (ceza büyüklüğü)
    [SerializeField] private int duplicateScanXpDefault = 100; // aynı kartı tekrar okuma ödülü (istiyorsan kullan)
    public int DuplicateScanXpDefault => duplicateScanXpDefault;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    // ----------------- Dış API -----------------

    /// <summary> Envanterde id ile kart bulup XP ekler/çıkarır. </summary>
    public void AddExperienceByKey(string cardId, int xpDelta)
    {
        var inv = PlayerInventory.Instance;
        if (inv == null || string.IsNullOrEmpty(cardId)) return;

        var c = inv.myCards.Find(x => x.id == cardId);
        if (c == null) return;

        AddExperience(c, xpDelta);
        inv.SaveToDisk();
    }

    /// <summary>
    /// Kartın XP’sini değiştirir. Pozitifte seviye atlatır, negatifte XP’yi 0’ın altına indirmez
    /// (de-level yapmıyoruz).
    /// </summary>
    public void AddExperience(CardData c, int xpDelta)
    {
        if (c == null) return;

        // Max level ise sadece XP’yi 0’da tut (arttırma yok)
        if (c.level >= maxLevel && xpDelta >= 0) return;

        // Negatif ceza: 0’ın altına düşürme, seviye düşürme yok
        if (xpDelta < 0)
        {
            c.xp = Mathf.Max(0, c.xp + xpDelta);
            return;
        }

        // Pozitif ilerleme
        c.xp += xpDelta;
        while (c.level < maxLevel && c.xp >= XpToNextLevel(c.level))
        {
            c.xp -= XpToNextLevel(c.level);
            c.level++;
            ApplyLevelGains(c, c.level);
        }
    }

    /// <summary>
    /// Kaydedilmiş bir kart yüklendikten sonra (Save/Load) görünür değerleri
    /// level’e göre yeniden kurmak için.
    /// </summary>
    public void ApplyToCard(CardData c)
    {
        if (c == null) return;

        int hp = c.baseHP;
        int str = c.baseDamage;
        int dx = (c.baseDex); // temel dex

        // Level 1 taban, 2..level arası her adımın getirisi eklenir
        for (int L = 2; L <= Mathf.Max(1, c.level); L++)
            AddRoleBasedGains(c, L, ref hp, ref str, ref dx);

        c.baseHP = hp;
        c.baseDamage = str;
        c.baseDex = dx; // Kart üstünde dex’i baseDEX’te tutuyoruz
    }

    // ----------------- XP eğrisi -----------------

    /// <summary>L -> (L+1) için gereken XP.</summary>
    public int XpToNextLevel(int level)
    {
        // float tabanlı; double/float uyumsuzluğu yok
        float v = 100f * Mathf.Pow(1.08f, Mathf.Max(0, level - 1));
        return Mathf.RoundToInt(v);
    }

    // Geriye uyumluluk: projede eski isim kullanıldıysa kırmızı vermesin
    public int XpForNextLevel(int level) => XpToNextLevel(level);

    // ----------------- Savaş Sonu XP hesapları -----------------

    /// <summary>Kazanınca kart başına verilecek XP (basit zorluk bonusu ile).</summary>
    public int ComputeWinXp(List<CardData> myUsed, List<CardData> oppUsed)
    {
        float myAvg = AvgLevel(myUsed);
        float oppAvg = AvgLevel(oppUsed);

        // Rakip ortalaması senden yüksekse her seviye farkı için %10 bonus
        float diff = Mathf.Max(0f, oppAvg - myAvg);
        float bonusMul = 1f + 0.10f * diff;

        return Mathf.RoundToInt(baseWinXp * bonusMul);
    }

    /// <summary>Kaybedince uygulanacak XP (negatif döner). “Kolay” rakibe kaybedersen ceza artar.</summary>
    public int ComputeLossXp(List<CardData> myUsed, List<CardData> oppUsed)
    {
        float myAvg = AvgLevel(myUsed);
        float oppAvg = AvgLevel(oppUsed);

        // Senden düşük rakibe kaybettiğinde her seviye farkı için %10 ekstra ceza
        float diff = Mathf.Max(0f, myAvg - oppAvg);
        float penMul = 1f + 0.10f * diff;

        return -Mathf.RoundToInt(baseLossXp * penMul); // negatif
    }

    private float AvgLevel(List<CardData> list)
    {
        if (list == null || list.Count == 0) return 1f;
        int sum = 0;
        for (int i = 0; i < list.Count; i++) sum += Mathf.Max(1, list[i].level);
        return (float)sum / list.Count;
    }

    // ----------------- Level artışı → stat artışı -----------------

    private enum Role { Tank, Warrior, Mage, Support, Assassin, Unknown }

    private Role ResolveRole(CardData c)
    {
        string n = (c.cardName ?? "").ToLowerInvariant();
        string r = (c.rarity ?? "").ToLowerInvariant();
        string a = (c.ability ?? "").ToLowerInvariant();

        if (n.Contains("suikast") || n.Contains("assassin") || n.Contains("rogue")) return Role.Assassin;
        if (n.Contains("tank") || r.Contains("tank")) return Role.Tank;
        if (n.Contains("büy") || n.Contains("mage") || a.Contains("büy")) return Role.Mage;
        if (n.Contains("destek") || n.Contains("support") || a.Contains("heal")) return Role.Support;
        if (n.Contains("savaş") || n.Contains("war") || r.Contains("fighter")) return Role.Warrior;

        return Role.Unknown;
    }

    private bool IsLegendaryGroup(CardData c)
    {
        string r = (c.rarity ?? "").ToLowerInvariant();
        return r.Contains("efsane") || r.Contains("legend") || r.Contains("multi");
    }

    /// <summary>Her level adımı için rol bazlı artışları ekler.</summary>
    private void AddRoleBasedGains(CardData c, int levelStep, ref int hp, ref int str, ref int dex)
    {
        var role = ResolveRole(c);
        bool legendary = IsLegendaryGroup(c);

        switch (role)
        {
            case Role.Tank: hp += 14; str += 2; dex += 6; break;
            case Role.Warrior: hp += 10; str += 8; dex += 2; break;
            case Role.Mage: hp += 10; str += 6; dex += 4; break;
            case Role.Support: hp += 10; str += 3; dex += 3; break;
            case Role.Assassin: hp += 10; str += (legendary ? 11 : 9); dex += (legendary ? 2 : 1); break;
            default: hp += 10; str += 5; dex += 2; break; // güvenli varsayılan
        }
    }

    /// <summary>Seviye atladığında tek adımın getirilerini uygular.</summary>
    public void ApplyLevelGains(CardData c, int targetLevel)
    {
        int hp = c.baseHP;
        int str = c.baseDamage;
        int dx = c.baseDex;

        AddRoleBasedGains(c, targetLevel, ref hp, ref str, ref dx);

        c.baseHP = hp;
        c.baseDamage = str;
        c.baseDex = dx;
    }
}