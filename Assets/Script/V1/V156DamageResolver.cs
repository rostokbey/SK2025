using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared server-side damage entry point for V1.56 basic attacks and active skills.
/// Element names are retained in card descriptions/logs; the existing HealthSystem
/// has no elemental resistance/damage-type channel, so damage remains numeric.
/// </summary>
public static class V156DamageResolver
{
    public static int ApplyDamage(
        Character source,
        Character target,
        int rawDamage,
        bool isCritical,
        bool isArea,
        bool triggerDamagePassives = true,
        bool isActiveAbility = false)
    {
        if (target == null || rawDamage <= 0 || !target.IsServer || !target.IsAlive)
            return 0;

        // Invisible can be hit by AoE, but single-target attacks are rejected/zeroed.
        if (target.IsInvisible && !isArea)
            return 0;

        if (isActiveAbility && source != null && !string.IsNullOrWhiteSpace(source.cardData?.passive))
        {
            IPassiveAbility sourcePassive = AbilityCatalog.GetPassive(source.cardData.passive);
            if (sourcePassive is IV156ActiveDamageBonus activeDamageBonus)
                rawDamage += Mathf.Max(0, activeDamageBonus.ConsumeActiveDamageBonus(source));
        }

        float multiplier = 1f;
        if (source != null)
            multiplier *= GetTeamAuraMultiplier(source);

        multiplier *= Mathf.Max(0f, target.DamageTakenMultiplierNet.Value);
        int finalDamage = Mathf.Max(0, Mathf.RoundToInt(rawDamage * multiplier));
        if (finalDamage <= 0)
            return 0;

        HealthSystem health = target.GetComponent<HealthSystem>();
        if (health == null)
            return 0;

        int before = health.currentHP.Value + health.shield;
        health.ApplyDamageServerSide(finalDamage, isCritical);
        int after = health.currentHP.Value + health.shield;
        int applied = Mathf.Max(0, before - after);

        if (triggerDamagePassives && applied > 0)
        {
            IPassiveAbility passive = !string.IsNullOrWhiteSpace(target.cardData?.passive)
                ? AbilityCatalog.GetPassive(target.cardData.passive)
                : null;
            if (passive is IV156DamageTakenPassive damageTakenPassive)
            {
                AbilityContext context = BuildContext(target, source);
                damageTakenPassive.OnDamageTaken(context, applied, source);
            }
        }

        return applied;
    }

    private static float GetTeamAuraMultiplier(Character source)
    {
        BattleManager manager = BattleManager.Instance;
        if (manager == null)
            return 1f;

        BattleGrid ownGrid = manager.GetGridFor(source.isPlayer);
        if (ownGrid == null)
            return 1f;

        float multiplier = 1f;
        List<Character> allies = ownGrid.GetAllAlive();
        foreach (Character ally in allies)
        {
            if (ally == null || string.IsNullOrWhiteSpace(ally.cardData?.passive))
                continue;

            IPassiveAbility passive = AbilityCatalog.GetPassive(ally.cardData.passive);
            if (passive is IV156TeamDamageAura aura)
                multiplier *= Mathf.Max(0f, aura.GetDamageMultiplier(source));
        }

        return multiplier;
    }

    private static AbilityContext BuildContext(Character damaged, Character attacker)
    {
        BattleManager manager = BattleManager.Instance;
        bool side = damaged != null && damaged.isPlayer;
        return new AbilityContext
        {
            Source = damaged,
            PrimaryTarget = attacker,
            AllTargets = attacker != null ? new List<Character> { attacker } : new List<Character>(),
            OwnGrid = manager != null ? manager.GetGridFor(side) : null,
            EnemyGrid = manager != null ? manager.GetGridFor(!side) : null,
            CurrentRound = manager != null ? manager.CurrentRound : 1
        };
    }
}
