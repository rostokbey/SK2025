using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Yetenekleri CardData.ability / CardData.passive alanındaki metin kimliğine göre
/// bulan basit bir kayıt (registry). Yeni bir yetenek eklemek için:
///   1) IActiveAbility ya da IPassiveAbility'yi implemente eden yeni bir sınıf yazın.
///   2) Aşağıdaki statik constructor'da Register(...) ile ekleyin.
///   3) Kartın "ability" (aktif) veya "passive" (pasif) alanına bu Id'yi yazın
///      (Card Signing Tool / JSON kart veritabanı üzerinden).
///
/// NOT: Bu turda sadece çerçeveyi ve BİRER örnek yetenek kurdum (gerçek yetenek
/// içerikleri - kim ne kadar hasar/heal/buff verecek - tasarım dokümanında
/// belirtilmemişti). Gerçek yetenek listesi netleşince buraya eklemek, cooldown/
/// hedefleme sistemine hiç dokunmadan yapılabilir.
/// </summary>
public static class AbilityCatalog
{
    private static readonly Dictionary<string, IActiveAbility> _active = new Dictionary<string, IActiveAbility>();
    private static readonly Dictionary<string, IPassiveAbility> _passive = new Dictionary<string, IPassiveAbility>();

    static AbilityCatalog()
    {
        RegisterActive(new PowerStrikeActiveAbility(), "power strike", "güçlü vuruş", "guclu vurus");
        RegisterActive(new WhisperActiveAbility(), "fisilti", "whisper");
        RegisterPassive(new RegenerationPassiveAbility(), "yenilenme", "rejenerasyon");
    }

    public static void RegisterActive(IActiveAbility ability, params string[] aliases)
    {
        if (ability == null || string.IsNullOrEmpty(ability.Id)) return;

        _active[NormalizeId(ability.Id)] = ability;
        if (aliases == null) return;

        foreach (string alias in aliases)
            if (!string.IsNullOrWhiteSpace(alias))
                _active[NormalizeId(alias)] = ability;
    }

    public static void RegisterPassive(IPassiveAbility ability, params string[] aliases)
    {
        if (ability == null || string.IsNullOrEmpty(ability.Id)) return;

        _passive[NormalizeId(ability.Id)] = ability;
        if (aliases == null) return;

        foreach (string alias in aliases)
            if (!string.IsNullOrWhiteSpace(alias))
                _passive[NormalizeId(alias)] = ability;
    }

    public static IActiveAbility GetActive(string id) =>
        !string.IsNullOrEmpty(id) && _active.TryGetValue(NormalizeId(id), out var a) ? a : null;

    public static IPassiveAbility GetPassive(string id) =>
        !string.IsNullOrEmpty(id) && _passive.TryGetValue(NormalizeId(id), out var p) ? p : null;

    /// <summary>
    /// Kart üretiminde "Fısıltı", "fisilti", "power_strike" gibi farklı
    /// yazımların aynı kayıtla güvenli biçimde eşleşmesini sağlar.
    /// </summary>
    private static string NormalizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string lowered = value.Trim().ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');

        var result = new StringBuilder(lowered.Length);
        foreach (char character in lowered)
            if (char.IsLetterOrDigit(character))
                result.Append(character);

        return result.ToString();
    }
}

// ---------------------------------------------------------------------------
// ÖRNEK AKTİF YETENEK: "power_strike"
// Hedefine baseStr'nin 2 katı kadar hasar verir. Card.ability = "power_strike"
// olan kartlar bu yeteneği kullanır.
// ---------------------------------------------------------------------------
public class PowerStrikeActiveAbility : IActiveAbility
{
    public string Id => "power_strike";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.PrimaryTarget == null) return;

        var targetHealth = context.PrimaryTarget.GetComponent<HealthSystem>();
        if (targetHealth == null) return;

        int attack = context.Source.cardData.str > 0
            ? context.Source.cardData.str
            : context.Source.cardData.baseStr;
        int defence = context.PrimaryTarget.cardData.def > 0
            ? context.PrimaryTarget.cardData.def
            : context.PrimaryTarget.cardData.baseDef;
        int damage = Mathf.Max(1, attack * 2 - defence);
        targetHealth.ApplyDamageServerSide(damage, false);

        Debug.Log($"[Ability:power_strike] {context.Source.cardData.cardName} -> " +
                  $"{context.PrimaryTarget.cardData.cardName} : {damage} hasar");
    }
}

// ---------------------------------------------------------------------------
// AKTİF YETENEK: "Fısıltı"
// Arka hattı öncelikli hedefler ve STR'nin %150'sinden hedef DEF'ini düşerek
// sunucuda hasar hesaplar. cards.json içindeki mevcut "Fısıltı" değeriyle
// doğrudan çalışır.
// ---------------------------------------------------------------------------
public class WhisperActiveAbility : IActiveAbility
{
    public string Id => "Fısıltı";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.IgnoreFrontline;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.PrimaryTarget == null) return;

        HealthSystem targetHealth = context.PrimaryTarget.GetComponent<HealthSystem>();
        if (targetHealth == null) return;

        int attack = context.Source.cardData.str > 0
            ? context.Source.cardData.str
            : context.Source.cardData.baseStr;
        int defence = context.PrimaryTarget.cardData.def > 0
            ? context.PrimaryTarget.cardData.def
            : context.PrimaryTarget.cardData.baseDef;
        int damage = Mathf.Max(1, Mathf.RoundToInt(attack * 1.5f) - defence);

        targetHealth.ApplyDamageServerSide(damage, false);

        Debug.Log($"[Ability:Fısıltı] {context.Source.cardData.cardName} -> " +
                  $"{context.PrimaryTarget.cardData.cardName} : {damage} hasar");
    }
}

// ---------------------------------------------------------------------------
// ÖRNEK PASİF YETENEK: "regeneration"
// Her raund başında maksimum canın %5'i kadar iyileşir. Card.passive =
// "regeneration" olan kartlar bu pasife sahiptir - sürekli devrededir, elle
// tetiklenmez.
// ---------------------------------------------------------------------------
public class RegenerationPassiveAbility : IPassiveAbility
{
    public string Id => "regeneration";

    public void OnSpawn(AbilityContext context) { /* bu pasif için spawn anında ek işlem yok */ }

    public void OnRoundStart(AbilityContext context)
    {
        if (context?.Source == null || !context.Source.IsAlive) return;

        var health = context.Source.GetComponent<HealthSystem>();
        if (health == null) return;

        int healAmount = Mathf.Max(1, Mathf.RoundToInt(context.Source.maxHp * 0.05f));
        health.HealServerSide(healAmount);

        Debug.Log($"[Ability:regeneration] {context.Source.cardData.cardName} +{healAmount} HP (raund {context.CurrentRound})");
    }
}
