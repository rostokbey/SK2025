using System.Collections.Generic;
using System.Linq;
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
        RegisterActive(new PowerStrikeActiveAbility());
        RegisterPassive(new RegenerationPassiveAbility());
    }

    public static void RegisterActive(IActiveAbility ability)
    {
        if (ability == null || string.IsNullOrEmpty(ability.Id)) return;
        _active[ability.Id] = ability;
    }

    public static void RegisterPassive(IPassiveAbility ability)
    {
        if (ability == null || string.IsNullOrEmpty(ability.Id)) return;
        _passive[ability.Id] = ability;
    }

    public static IActiveAbility GetActive(string id) =>
        !string.IsNullOrEmpty(id) && _active.TryGetValue(id, out var a) ? a : null;

    public static IPassiveAbility GetPassive(string id) =>
        !string.IsNullOrEmpty(id) && _passive.TryGetValue(id, out var p) ? p : null;
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

        int damage = Mathf.Max(1, context.Source.cardData.baseStr * 2);
        targetHealth.TakeDamageServerRpc(damage);

        Debug.Log($"[Ability:power_strike] {context.Source.cardData.cardName} -> " +
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
