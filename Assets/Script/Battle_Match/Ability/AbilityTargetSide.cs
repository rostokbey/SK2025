/// <summary>
/// Aktif yeteneğin hedef tarafı. Varsayılan Enemy'dir (mevcut davranış).
/// İyileştirme / buff / kendine uygulanan yetenekler IAbilityTargetSide
/// arayüzünü uygulayarak farklı bir taraf bildirir; mevcut yeteneklerin
/// hiçbir değişiklik yapmasına gerek yoktur.
/// </summary>
public enum AbilityTargetSide
{
    Enemy = 0,
    Ally = 1,
    Self = 2
}

/// <summary>
/// Opsiyonel arayüz. IActiveAbility uygulayan sınıf bunu da uygularsa
/// BattleManager hedefi düşman yerine ilgili taraftan çözer.
/// Örnek: public class HealingLight : IActiveAbility, IAbilityTargetSide
///        { public AbilityTargetSide TargetSide => AbilityTargetSide.Ally; ... }
/// </summary>
public interface IAbilityTargetSide
{
    AbilityTargetSide TargetSide { get; }
}
