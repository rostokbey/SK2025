using System.Collections.Generic;

/// <summary>
/// Bir yeteneğin (aktif ya da pasif) çalıştırılması için gereken tüm bağlamı taşır.
/// Yetenek implementasyonları sahneye/NetworkManager'a doğrudan erişmez, sadece bu
/// context üzerinden çalışır - test edilebilirliği ve tutarlılığı korur.
/// </summary>
public class AbilityContext
{
    /// <summary>Yeteneği kullanan karakter.</summary>
    public Character Source;

    /// <summary>Sunucu tarafında hesaplanmış, kurallara uygun birincil hedef (null olabilir - ör. kendine buff).</summary>
    public Character PrimaryTarget;

    /// <summary>AoE gibi çoklu hedefli yetenekler için hedef listesi (PrimaryTarget da içinde olabilir).</summary>
    public List<Character> AllTargets;

    /// <summary>Bu yeteneğin ait olduğu tarafın BattleGrid'i (kendi hattını sorgulamak için).</summary>
    public BattleGrid OwnGrid;

    /// <summary>Karşı tarafın BattleGrid'i (hedef sorguları için).</summary>
    public BattleGrid EnemyGrid;

    /// <summary>Şu anki raund numarası (loglama/koşullu efektler için).</summary>
    public int CurrentRound;
}
