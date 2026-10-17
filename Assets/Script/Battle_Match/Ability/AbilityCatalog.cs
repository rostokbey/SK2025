using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Registry of the V1.56 active and passive card effects.</summary>
public static class AbilityCatalog
{
    private static readonly Dictionary<string, IActiveAbility> active =
        new Dictionary<string, IActiveAbility>();
    private static readonly Dictionary<string, IPassiveAbility> passive =
        new Dictionary<string, IPassiveAbility>();

    static AbilityCatalog()
    {
        // BÜYÜCÜ — Aktifler
        RegisterActive(new AlevTopuActiveAbility(), "Alev Topu", "AlevTopu", "Ates_Ciragi", "188", "Fireball");
        RegisterActive(new BuzDikenleriActiveAbility(), "Buz Dikenleri", "BuzDikenleri", "Buz_Ogrencisi", "189");
        RegisterActive(new ToprakCokusuActiveAbility(), "Toprak Çöküşü", "ToprakCokusu", "Kita_Buyucusu", "192");
        RegisterActive(new KozmikCarpmaActiveAbility(), "Kozmik Çarpma", "KozmikCarpma", "Galaktik_Bilgisi", "217");
        RegisterActive(new ArkanPatlamasiActiveAbility(), "Arkan Patlaması", "ArkanPatlamasi", "Run_Ustasi_Buyucusu", "221");
        RegisterActive(new ElementFirtinasiActiveAbility(), "Element Fırtınası", "ElementFirtinasi", "Vahsi_Elementalist", "228");
        RegisterActive(new AkilCelmesiActiveAbility(), "Akıl Çelmesi", "AkilCelmesi", "Mistik_Ogretmen", "234");
        RegisterActive(new ZamanDurmasiActiveAbility(), "Zaman Durması", "ZamanDurmasi", "Zaman_Buyucusu", "236");
        RegisterActive(new YildirimCigligiActiveAbility(), "Yıldırım Çığlığı", "YildirimCigligi", "Firtina_Cildirani", "239");
        RegisterActive(new YasaminOfkesiActiveAbility(), "Yaşamın Öfkesi", "YasaminOfkesi", "Kadim_Buyucu", "244");
        RegisterActive(new TanrisalBuyuActiveAbility(), "Tanrısal Büyü", "TanrisalBuyu", "Buyu_Tanrisi", "248");

        // BÜYÜCÜ — Pasifler
        RegisterPassive(new AlevOzuPassiveAbility(), "Alev Özü", "AlevOzu", "Ates_Ciragi", "188");
        RegisterPassive(new BuzulZirhPassiveAbility(), "Buzul Zırh", "BuzulZirh", "Buz_Ogrencisi", "189");
        RegisterPassive(new ToprakHafizasiPassiveAbility(), "Toprak Hafızası", "ToprakHafizasi", "Kita_Buyucusu", "192");
        RegisterPassive(new YildizTozuPassiveAbility(), "Yıldız Tozu", "YildizTozu", "Galaktik_Bilgisi", "217");
        RegisterPassive(new RunBirikimiPassiveAbility(), "Rün Birikimi", "RunBirikimi", "Run_Ustasi_Buyucusu", "221");
        RegisterPassive(new VahsiDonguPassiveAbility(), "Vahşi Döngü", "VahsiDongu", "Vahsi_Elementalist", "228");
        RegisterPassive(new ZihinKalkaniPassiveAbility(), "Zihin Kalkanı", "ZihinKalkani", "Mistik_Ogretmen", "234");
        RegisterPassive(new ZamanAkisiPassiveAbility(), "Zaman Akışı", "ZamanAkisi", "Zaman_Buyucusu", "236");
        RegisterPassive(new StatikYukPassiveAbility(), "Statik Yük", "StatikYuk", "Firtina_Cildirani", "239");
        RegisterPassive(new KadimBilgelikMagePassiveAbility(), "Kadim Bilgelik", "KadimBilgelik", "Kadim_Buyucu", "244");
        RegisterPassive(new SinirsizIradePassiveAbility(), "Sınırsız İrade", "SinirsizIrade", "Buyu_Tanrisi", "248");
    }

    public static void RegisterActive(IActiveAbility ability, params string[] aliases)
    {
        if (ability == null || string.IsNullOrWhiteSpace(ability.Id))
            return;

        active[NormalizeId(ability.Id)] = ability;
        if (aliases == null)
            return;

        foreach (string alias in aliases)
            if (!string.IsNullOrWhiteSpace(alias))
                active[NormalizeId(alias)] = ability;
    }

    public static void RegisterPassive(IPassiveAbility ability, params string[] aliases)
    {
        if (ability == null || string.IsNullOrWhiteSpace(ability.Id))
            return;

        passive[NormalizeId(ability.Id)] = ability;
        if (aliases == null)
            return;

        foreach (string alias in aliases)
            if (!string.IsNullOrWhiteSpace(alias))
                passive[NormalizeId(alias)] = ability;
    }

    public static IActiveAbility GetActive(string id) =>
        !string.IsNullOrWhiteSpace(id) && active.TryGetValue(NormalizeId(id), out var value)
            ? value
            : null;

    public static IPassiveAbility GetPassive(string id) =>
        !string.IsNullOrWhiteSpace(id) && passive.TryGetValue(NormalizeId(id), out var value)
            ? value
            : null;

    private static string NormalizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string lowered = value.Trim().ToLowerInvariant()
            .Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i')
            .Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');

        var result = new StringBuilder(lowered.Length);
        foreach (char character in lowered)
            if (char.IsLetterOrDigit(character))
                result.Append(character);
        return result.ToString();
    }
}

internal static class V156AbilityMath
{
    public static int RoundHalfUp(float value) => Mathf.FloorToInt(value + 0.5f);

    public static int BaseMaxHp(Character character)
    {
        if (character == null) return 0;
        if (character.MaxHpNet.Value > 0) return character.MaxHpNet.Value;
        return character.cardData != null ? character.cardData.baseHP : 0;
    }

    public static int Deal(Character source, Character target, int damage, bool isArea = false)
    {
        return V156DamageResolver.ApplyDamage(
            source, target, damage, false, isArea,
            triggerDamagePassives: true,
            isActiveAbility: true);
    }

    public static void HealPercent(Character target, float percent)
    {
        if (target == null || !target.IsAlive || percent <= 0f) return;
        HealthSystem health = target.GetComponent<HealthSystem>();
        if (health == null) return;
        int amount = RoundHalfUp(BaseMaxHp(target) * percent / 100f);
        if (amount > 0) health.HealServerSide(amount);
    }

    public static void HealTeam(BattleGrid grid, float percent)
    {
        if (grid == null) return;
        foreach (Character ally in grid.GetAllAlive())
            HealPercent(ally, percent);
    }

    public static Character LowestHp(BattleGrid grid)
    {
        if (grid == null) return null;
        Character lowest = null;
        int lowestHp = int.MaxValue;
        foreach (Character ally in grid.GetAllAlive())
        {
            HealthSystem health = ally != null ? ally.GetComponent<HealthSystem>() : null;
            if (health == null) continue;
            if (health.currentHP.Value < lowestHp)
            {
                lowest = ally;
                lowestHp = health.currentHP.Value;
            }
        }
        return lowest;
    }

    public static float TeamAverageHpPercent(BattleGrid grid)
    {
        if (grid == null) return 100f;
        List<Character> allies = grid.GetAllAlive();
        if (allies.Count == 0) return 100f;
        float sum = 0f;
        int count = 0;
        foreach (Character ally in allies)
        {
            HealthSystem health = ally != null ? ally.GetComponent<HealthSystem>() : null;
            int max = BaseMaxHp(ally);
            if (health == null || max <= 0) continue;
            sum += Mathf.Clamp01((float)health.currentHP.Value / max) * 100f;
            count++;
        }
        return count == 0 ? 100f : sum / count;
    }
}

// -----------------------------------------------------------------------------
// BÜYÜCÜ AKTİF YETENEKLER
// -----------------------------------------------------------------------------
public sealed class AlevTopuActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "AlevTopu";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.SingleEnemy;
    public int RequiredTargets => 1;

    public void Apply(AbilityContext context)
    {
        Character source = context?.Source;
        Character target = context?.PrimaryTarget;
        if (source == null || target == null || !source.IsServer || !target.IsAlive) return;
        int damage = Mathf.Max(1, V156AbilityMath.RoundHalfUp(source.AttackNet.Value * 1.2f));
        V156AbilityMath.Deal(source, target, damage);
        if (target.IsAlive) target.ApplyBlindServerSide(1);
        Debug.Log($"[Ability:AlevTopu] {source.cardData?.cardName} -> {target.cardData?.cardName}: {damage} Ateş.");
    }
}

public sealed class BuzDikenleriActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "BuzDikenleri";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.MultiEnemy;
    public int RequiredTargets => 3;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.AllTargets == null) return;
        foreach (Character target in context.AllTargets)
        {
            if (target == null || !target.IsAlive) continue;
            V156AbilityMath.Deal(context.Source, target, 13, true);
            if (target.IsAlive) target.ApplyBlindServerSide(1);
        }
    }
}

public sealed class ToprakCokusuActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "ToprakCokusu";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.AreaOfEffect;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.AllEnemies;
    public int RequiredTargets => 0;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.AllTargets == null) return;
        foreach (Character target in context.AllTargets)
        {
            if (target == null || !target.IsAlive) continue;
            V156AbilityMath.Deal(context.Source, target, 13, true);
            if (target.IsAlive) target.ApplyDefenseBreakServerSide(15, 2);
        }
    }
}

public sealed class KozmikCarpmaActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "KozmikCarpma";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.SingleEnemy;
    public int RequiredTargets => 1;

    public void Apply(AbilityContext context)
    {
        Character source = context?.Source;
        Character target = context?.PrimaryTarget;
        if (source == null || target == null || !target.IsAlive) return;
        V156AbilityMath.Deal(source, target, 26);
        if (target.IsAlive) target.ApplyStunServerSide(2);
    }
}

public sealed class ArkanPatlamasiActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "ArkanPatlamasi";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.SingleEnemy;
    public int RequiredTargets => 1;

    public void Apply(AbilityContext context)
    {
        Character source = context?.Source;
        Character target = context?.PrimaryTarget;
        if (source == null || target == null || !target.IsAlive) return;
        bool empowered = source.RuneStacksNet.Value >= 3;
        V156AbilityMath.Deal(source, target, empowered ? 72 : 48);
        if (empowered) source.ConsumeThreeRunesServerSide();
        if (target.IsAlive) target.ApplySilenceServerSide(4);
    }
}

public sealed class ElementFirtinasiActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "ElementFirtinasi";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.AreaOfEffect;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.AllEnemies;
    public int RequiredTargets => 0;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.AllTargets == null) return;
        foreach (Character target in context.AllTargets)
        {
            if (target == null || !target.IsAlive) continue;
            V156AbilityMath.Deal(context.Source, target, 27, true);
            if (target.IsAlive) target.ApplyFearServerSide(2);
        }
    }
}

public sealed class AkilCelmesiActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "AkilCelmesi";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.SingleEnemy;
    public int RequiredTargets => 1;

    public void Apply(AbilityContext context)
    {
        Character source = context?.Source;
        Character target = context?.PrimaryTarget;
        if (source == null || target == null || !target.IsAlive) return;
        V156AbilityMath.Deal(source, target, 48);
        if (target.IsAlive) target.ApplyStunServerSide(2);
    }
}

public sealed class ZamanDurmasiActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "ZamanDurmasi";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.Normal;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.MultiEnemy;
    public int RequiredTargets => 2;

    public void Apply(AbilityContext context)
    {
        if (context?.AllTargets == null) return;
        foreach (Character target in context.AllTargets)
            if (target != null && target.IsAlive)
                target.ApplyStunServerSide(2);
    }
}

public sealed class YildirimCigligiActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "YildirimCigligi";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.AreaOfEffect;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.AllEnemies;
    public int RequiredTargets => 0;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.AllTargets == null) return;
        foreach (Character target in context.AllTargets)
        {
            if (target == null || !target.IsAlive) continue;
            V156AbilityMath.Deal(context.Source, target, 44, true);
            if (target.IsAlive) target.ApplyDefenseBreakServerSide(20, 2);
        }
    }
}

public sealed class YasaminOfkesiActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility
{
    public string Id => "YasaminOfkesi";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.AreaOfEffect;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.AllEnemies;
    public int RequiredTargets => 0;

    public void Apply(AbilityContext context)
    {
        if (context?.Source == null || context.AllTargets == null) return;
        foreach (Character target in context.AllTargets)
            if (target != null && target.IsAlive)
                V156AbilityMath.Deal(context.Source, target, 67, true);

        if (V156AbilityMath.TeamAverageHpPercent(context.OwnGrid) < 30f)
            V156AbilityMath.HealTeam(context.OwnGrid, 25f);
    }
}

public sealed class TanrisalBuyuActiveAbility : IActiveAbility, IAbilityTargetSide, IV156TargetedAbility, IV156SuppressVfx
{
    public string Id => "TanrisalBuyu";
    public AbilityTargetingMode TargetingMode => AbilityTargetingMode.AreaOfEffect;
    public AbilityTargetSide TargetSide => AbilityTargetSide.Enemy;
    public V156TargetRule TargetRule => V156TargetRule.AllEnemies;
    public int RequiredTargets => 0;

    public void Apply(AbilityContext context)
    {
        Character source = context?.Source;
        if (source == null) return;
        if (context.AllTargets != null)
            foreach (Character target in context.AllTargets)
                if (target != null && target.IsAlive)
                    V156AbilityMath.Deal(source, target, 112, true);
        source.ApplyInvisibleServerSide(2, true);
    }
}

// -----------------------------------------------------------------------------
// BÜYÜCÜ PASİFLER
// -----------------------------------------------------------------------------
public sealed class AlevOzuPassiveAbility : IPassiveAbility, IV156NormalAttackPassive
{
    public string Id => "AlevOzu";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) => context?.Source?.ArmAlevOzuServerSide();

    public void BeforeNormalAttack(AbilityContext context, ref int damage, ref bool critical)
    {
        if (context?.Source != null && context.Source.ConsumeAlevOzuChargeServerSide())
            damage += 5;
    }

    public void AfterNormalAttack(AbilityContext context, int damageApplied, bool critical)
    {
        Character source = context?.Source;
        Character target = context?.PrimaryTarget;
        if (source != null && source.ConsumeAlevOzuPendingBurnServerSide() &&
            target != null && target.IsAlive)
            target.ApplyDotServerSide(V156DotKind.Burn, 3, 2);
    }
}

public sealed class BuzulZirhPassiveAbility : IPassiveAbility
{
    public string Id => "BuzulZirh";
    public void OnSpawn(AbilityContext context)
    {
        HealthSystem health = context?.Source != null ? context.Source.GetComponent<HealthSystem>() : null;
        int defense = context?.Source != null ? context.Source.DefenseNet.Value : 0;
        if (health != null) health.AddShieldServerSide(V156AbilityMath.RoundHalfUp(defense * 0.5f));
    }
    public void OnRoundStart(AbilityContext context) { }
}

public sealed class ToprakHafizasiPassiveAbility : IPassiveAbility, IV156DamageTakenPassive, IV156ActiveDamageBonus
{
    public string Id => "ToprakHafizasi";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) { }
    public void OnDamageTaken(AbilityContext context, int damageApplied, Character attacker)
    {
        context?.Source?.StoreAbilityDamageBonusServerSide(V156AbilityMath.RoundHalfUp(damageApplied * 0.10f));
    }
    public int ConsumeActiveDamageBonus(Character source) =>
        source != null ? source.ConsumeStoredAbilityDamageBonusServerSide() : 0;
}

public sealed class YildizTozuPassiveAbility : IPassiveAbility
{
    public string Id => "YildizTozu";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context)
    {
        Character lowest = V156AbilityMath.LowestHp(context?.OwnGrid);
        V156AbilityMath.HealPercent(lowest, 5f);
    }
}

public sealed class RunBirikimiPassiveAbility : IPassiveAbility
{
    public string Id => "RunBirikimi";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) => context?.Source?.AddRuneServerSide(1);
}

public sealed class VahsiDonguPassiveAbility : IPassiveAbility
{
    public string Id => "VahsiDongu";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) =>
        context?.Source?.ApplyStrengthBuffServerSide(5, 1, 3);
}

public sealed class ZihinKalkaniPassiveAbility : IPassiveAbility, IV156CcDurationModifier
{
    public string Id => "ZihinKalkani";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) { }
    public int ModifyCcDuration(Character target, int baseRounds) => Mathf.Max(0, baseRounds - 1);
}

public sealed class ZamanAkisiPassiveAbility : IPassiveAbility
{
    public string Id => "ZamanAkisi";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) => context?.Source?.ReduceActiveCooldownServerSide(1);
}

public sealed class StatikYukPassiveAbility : IPassiveAbility, IV156DamageTakenPassive, IV156NormalAttackPassive
{
    public string Id => "StatikYuk";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) { }
    public void OnDamageTaken(AbilityContext context, int damageApplied, Character attacker) =>
        context?.Source?.AddStaticChargeServerSide(5);
    public void BeforeNormalAttack(AbilityContext context, ref int damage, ref bool critical)
    {
        if (context?.Source != null)
            damage += context.Source.ConsumeStaticChargeServerSide();
    }
    public void AfterNormalAttack(AbilityContext context, int damageApplied, bool critical) { }
}

public sealed class KadimBilgelikMagePassiveAbility : IPassiveAbility, IV156TeamDamageAura
{
    public string Id => "KadimBilgelikMage";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context) { }
    public float GetDamageMultiplier(Character ally) => 1.10f;
}

public sealed class SinirsizIradePassiveAbility : IPassiveAbility
{
    public string Id => "SinirsizIrade";
    public void OnSpawn(AbilityContext context) { }
    public void OnRoundStart(AbilityContext context)
    {
        BattleGrid grid = context?.OwnGrid;
        if (grid == null) return;
        Character lowest = null;
        int lowestDef = int.MaxValue;
        foreach (Character ally in grid.GetAllAlive())
        {
            int def = ally != null ? ally.DefenseNet.Value : int.MaxValue;
            if (def < lowestDef)
            {
                lowest = ally;
                lowestDef = def;
            }
        }
        if (lowest == null) return;
        HealthSystem health = lowest.GetComponent<HealthSystem>();
        if (health != null)
            health.AddShieldServerSide(V156AbilityMath.RoundHalfUp(lowestDef * 0.20f));
    }
}
