/// <summary>Target resolution styles used by the V1.56 card definitions.</summary>
public enum V156DotKind
{
    Burn,
    Bleed,
    Poison
}

public enum V156TargetRule
{
    SingleEnemy,
    MultiEnemy,
    AllEnemies,
    Self,
    SingleAlly,
    LowestHpAlly,
    AllAllies
}

/// <summary>
/// Optional metadata implemented by V1.56 abilities. The server and selection UI
/// use the same rule, so client-side target choices are always validated again.
/// </summary>
public interface IV156TargetedAbility
{
    V156TargetRule TargetRule { get; }
    int RequiredTargets { get; }
}

/// <summary>Optional passive hook for modifiers that shorten incoming CC.</summary>
public interface IV156CcDurationModifier
{
    int ModifyCcDuration(Character target, int baseRounds);
}

/// <summary>Passive abilities that explicitly trigger on every personal tick.</summary>
public interface IV156PersonalTickPassive
{
    void OnPersonalTick(AbilityContext context);
}

/// <summary>Hooks for card passives triggered by a normal attack or damage received.</summary>
public interface IV156NormalAttackPassive
{
    void BeforeNormalAttack(AbilityContext context, ref int damage, ref bool critical);
    void AfterNormalAttack(AbilityContext context, int damageApplied, bool critical);
}

public interface IV156DamageTakenPassive
{
    void OnDamageTaken(AbilityContext context, int damageApplied, Character attacker);
}

public interface IV156TeamDamageAura
{
    float GetDamageMultiplier(Character ally);
}

public interface IV156ActiveDamageBonus
{
    int ConsumeActiveDamageBonus(Character source);
}

public interface IV156SuppressVfx
{
}
