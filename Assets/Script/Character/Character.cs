using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using SK2025.VFX;

[RequireComponent(typeof(NetworkObject))]
public class Character : NetworkBehaviour
{
    [Header("Stats")]
    public CardData cardData;
    public float maxHp;
    public float defense = 0f;

    private HealthSystem health;
    private int baseCombatDefense;
    private int baseCombatAttack;

    [Header("State")]
    public readonly NetworkVariable<bool> IsMyTurnNet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<bool> IsPlayerNet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool isMyTurn => IsMyTurnNet.Value;
    public bool isPlayer => IsPlayerNet.Value;

    public readonly NetworkVariable<ulong> ControllerClientIdNet = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<FixedString64Bytes> DisplayNameNet = new NetworkVariable<FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> MaxHpNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> AttackNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> DefenseNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ulong ControllerClientId => ControllerClientIdNet.Value;

    // Kart verisi istemcilerde bulunmayabileceğinden hedef tarafı ağda eşitlenir.
    public readonly NetworkVariable<int> AbilityTargetSideNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public AbilityTargetSide ActiveAbilityTargetSide => (AbilityTargetSide)AbilityTargetSideNet.Value;

    [Header("Optional")]
    public Animator animator;

    [ClientRpc]
    public void PlayAnimationClientRpc(string triggerName)
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (animator == null)
        {
            Debug.LogError($"[ANIM] {name}: Animator bulunamadı. Trigger={triggerName}");
            return;
        }

        Debug.Log($"[ANIM] {name}: Trigger gönderildi → {triggerName}, " +
                  $"Controller={animator.runtimeAnimatorController?.name}");
        animator.ResetTrigger(triggerName);
        animator.SetTrigger(triggerName);
    }

    private void Awake()
    {
        health = GetComponent<HealthSystem>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsClient && GetComponent<CharacterWorldHealthBar>() == null)
            gameObject.AddComponent<CharacterWorldHealthBar>();

        if (IsServer)
            SubscribeToRoundTicks();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromRoundTicks();
        base.OnNetworkDespawn();
    }

    // -----------------------------------------------------------------
    #region Grid Konumu

    private readonly NetworkVariable<int> GridRowNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public BattleRow gridRow => (BattleRow)Mathf.Clamp(GridRowNet.Value, 0, 2);

    private readonly NetworkVariable<int> GridColumnNet = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int gridColumn => GridColumnNet.Value;

    public void SetGridPosition(BattleRow row, int column)
    {
        if (!IsServer) return;
        GridRowNet.Value = (int)row;
        GridColumnNet.Value = column;
    }

    #endregion

    // -----------------------------------------------------------------
    #region Yaşam Durumu

    public bool IsAlive => health != null && health.currentHP.Value > 0;

    /// <summary>
    /// Karakterin düşmüş/ayakta görselini TÜM oyuncularda uygular. Eskiden yalnızca sunucuda
    /// (host'ta) çalışıyordu: ölüm animasyonu ve collider kapanması client'a hiç ulaşmıyordu.
    /// Sunucu çağırır; ClientRpc host'ta da yerelde çalışır.
    /// </summary>
    public void SetDownedVisual(bool isDowned)
    {
        if (!IsServer)
            return;

        SetDownedVisualClientRpc(isDowned);

        // Host değil, saf sunucu ise ClientRpc yerelde çalışmaz.
        if (!IsClient)
            ApplyDownedVisual(isDowned);
    }

    [ClientRpc]
    private void SetDownedVisualClientRpc(bool isDowned)
    {
        ApplyDownedVisual(isDowned);
    }

    private void ApplyDownedVisual(bool isDowned)
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator != null)
        {
            animator.ResetTrigger("Die");
            if (isDowned)
                animator.SetTrigger("Die");
        }

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = !isDowned;
    }

    public void Revive(int hpAmount)
    {
        if (!IsServer || health == null) return;

        health.currentHP.Value = Mathf.Clamp(hpAmount, 1, health.maxHP);
        health.shield = 0;
        SetDownedVisual(false);
        Debug.Log($"[CHAR] {cardData?.cardName} yeniden dirildi. " +
                  $"Kalan aktif CD: {AbilityCooldownRemaining.Value}.");
    }

    #endregion

    // -----------------------------------------------------------------
    #region V1.56 Raund Tiki / Kişisel Sayaç / Aktif CD

    // V1.56: Global raund başında tüm canlı ve sersemletilmemiş karakterler
    // kişisel tik alır. Oyuncunun hangi karakteri seçtiği bu sayaçtan bağımsızdır.
    public readonly NetworkVariable<int> PersonalCounterNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int PersonalCounter => PersonalCounterNet.Value;

    // Aktif normalde 4 kişisel tikte açılır. CC_SILENCE açılış eşiğini
    // en fazla toplam +8 tik geciktirebilir.
    public readonly NetworkVariable<int> ActiveUnlockThresholdNet = new NetworkVariable<int>(
        4, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> AbilityCooldownRemaining = new NetworkVariable<int>(
        4, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Mevcut CD döngüsüne eklenen Silence cezası; CD sıfırlanınca temizlenir.
    public readonly NetworkVariable<int> ActiveCdSilencePenaltyNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> RuneStacksNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<bool> AlevOzuReadyNet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<bool> AlevOzuPendingBurnNet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> StoredAbilityDamageBonusNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> StaticChargeDamageNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> StrengthBuffStacksNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> StrengthBuffPercentPerStackNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> StrengthBuffRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // İlk aşamada AlevTopu'nun uyguladığı CC_BLIND ve altyapı için CC_STUN.
    // Süreler global raund başında azalır; Stun sayaç/CD/pasif ilerlemesini durdurur.
    public readonly NetworkVariable<int> BlindRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> FearRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> MarkRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> DamageTakenMultiplierNet = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> DefenseBreakRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> DefenseBreakPercentNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> TauntRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> BurnDamagePerTickNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> BurnRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> BleedDamagePerTickNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> BleedRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> PoisonDamagePerTickNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> PoisonRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> InvisibleRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<bool> InvisibleBreaksOnAttackNet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> StunRoundsRemainingNet = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<bool> LockActionThisRoundNet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsBlinded => BlindRoundsRemainingNet.Value > 0;
    public bool IsFeared => FearRoundsRemainingNet.Value > 0;
    public bool IsMarked => MarkRoundsRemainingNet.Value > 0;
    public bool IsTaunting => TauntRoundsRemainingNet.Value > 0;
    public bool IsInvisible => InvisibleRoundsRemainingNet.Value > 0;
    public int EffectiveDefense => Mathf.Max(0, DefenseNet.Value);
    // Geriye dönük BattleManager UI/uyarı kontrolü için türetilmiş durum:
    // Silence ayrı bir süreli CC değildir; yalnızca unlock eşiği/CD cezası sürerken true olur.
    public bool IsSilenced =>
        (ActiveUnlockThresholdNet.Value > 4 &&
         PersonalCounterNet.Value < ActiveUnlockThresholdNet.Value) ||
        ActiveCdSilencePenaltyNet.Value > 0;
    public bool IsStunned => StunRoundsRemainingNet.Value > 0 || LockActionThisRoundNet.Value;
    public bool CanUseNormalAction => IsAlive && !IsStunned && !IsFeared;
    public bool CanUseActiveAbility => IsAlive && !IsStunned && !IsBlinded;

    public bool IsActiveAbilityReady =>
        PersonalCounterNet.Value >= ActiveUnlockThresholdNet.Value &&
        AbilityCooldownRemaining.Value <= 0 &&
        CanUseActiveAbility;

    private int lastProcessedRound = -1;
    private BattleManager roundTickManager;

    private void SubscribeToRoundTicks()
    {
        BattleManager manager = BattleManager.Instance;
        if (manager == null)
            return;

        if (roundTickManager == manager)
            return;

        UnsubscribeFromRoundTicks();
        roundTickManager = manager;
        roundTickManager.RoundNumber.OnValueChanged += HandleRoundNumberChanged;

        // Karakter savaş başladıktan sonra sahaya girdiyse mevcut raundun
        // tikini geriye dönük almaz; ilk tiki bir sonraki raund başındadır.
        if (lastProcessedRound < 0)
            lastProcessedRound = roundTickManager.RoundNumber.Value;
    }

    private void UnsubscribeFromRoundTicks()
    {
        if (roundTickManager != null)
            roundTickManager.RoundNumber.OnValueChanged -= HandleRoundNumberChanged;
        roundTickManager = null;
    }

    private void HandleRoundNumberChanged(int previousRound, int newRound)
    {
        if (newRound < previousRound)
            lastV156ActiveRoundByClient.Clear();

        if (!IsServer || newRound <= 0)
            return;

        // BattleManager ayrıca AdvanceRound içinde OnRoundStartServer çağırıyor.
        // lastProcessedRound çift tik alınmasını engeller.
        OnRoundStartServer(newRound);
    }

    /// <summary>
    /// Her global raund başında çağrılır. Round 1 başlangıcı NetworkVariable
    /// değişimiyle, sonraki raundlar BattleManager.AdvanceRound ile işlenir.
    /// </summary>
    public void OnRoundStartServer(int currentRound)
    {
        if (!IsServer || currentRound <= 0)
            return;

        // Maç aynı nesnelerle yeniden başlatılırsa önce raund serisini sıfırla.
        // Bu kontrolün tekrarlı-çağrı korumasından önce olması gerekir.
        if (currentRound < lastProcessedRound)
            lastProcessedRound = currentRound - 1;

        if (currentRound <= lastProcessedRound)
            return;

        lastProcessedRound = currentRound;
        LockActionThisRoundNet.Value = false;

        // Pseudocode AliveCharacters döngüsüne eşdeğer: düşmüş karakter zaman almaz.
        if (!IsAlive)
            return;

        // Pasif tetikleme, raund başındaki durumun kör olup olmadığına göre yapılır.
        // Blind süresi yine global raundda azalır; ancak bu raunddaki pasifi durdurur.
        bool wasBlindedAtRoundStart = IsBlinded;

        if (StunRoundsRemainingNet.Value > 0)
        {
            LockActionThisRoundNet.Value = true;
            StunRoundsRemainingNet.Value = Mathf.Max(0, StunRoundsRemainingNet.Value - 1);
            TickOtherGlobalStatuses();

            // Sersemletme bu raundda sıfırlansa bile aksiyon kilidi raund sonuna
            // kadar sürer; normal zaman bir sonraki raund başında devam eder.
            Debug.Log($"[V1.56] {cardData?.cardName} sersemletildi; kişisel saat/CD/pasif dondu. " +
                      $"Stun kalan={StunRoundsRemainingNet.Value}, raund={currentRound}.");
            return;
        }

        PersonalCounterNet.Value++;
        if (AbilityCooldownRemaining.Value > 0)
        {
            AbilityCooldownRemaining.Value--;
            if (AbilityCooldownRemaining.Value == 0)
                ActiveCdSilencePenaltyNet.Value = 0;
        }

        // Önceden uygulanmış global süreleri bu tikte azalt; böylece bu tikte
        // pasifin verdiği yeni buff/CC aynı anda eksilmez.
        TickOtherGlobalStatuses();

        // Körlük raund başında varsa hem iki-tik pasifini hem de her-tik pasifini durdurur.
        if (IsAlive && !wasBlindedAtRoundStart && !string.IsNullOrWhiteSpace(cardData?.passive))
        {
            IPassiveAbility passive = AbilityCatalog.GetPassive(cardData.passive);
            AbilityContext passiveContext = BuildContext(null, currentRound);

            if (passive is IV156PersonalTickPassive perTickPassive)
                perTickPassive.OnPersonalTick(passiveContext);

            if (PersonalCounterNet.Value % 2 == 0)
                passive?.OnRoundStart(passiveContext);
        }

        Debug.Log($"[V1.56] {cardData?.cardName}: PersonalCounter={PersonalCounterNet.Value}, " +
                  $"ActiveCD={AbilityCooldownRemaining.Value}, Round={currentRound}.");
    }

    private void TickOtherGlobalStatuses()
    {
        if (BlindRoundsRemainingNet.Value > 0)
            BlindRoundsRemainingNet.Value--;
        if (FearRoundsRemainingNet.Value > 0)
            FearRoundsRemainingNet.Value--;
        if (MarkRoundsRemainingNet.Value > 0)
        {
            MarkRoundsRemainingNet.Value--;
            if (MarkRoundsRemainingNet.Value == 0)
                DamageTakenMultiplierNet.Value = 1f;
        }
        if (DefenseBreakRoundsRemainingNet.Value > 0)
        {
            DefenseBreakRoundsRemainingNet.Value--;
            if (DefenseBreakRoundsRemainingNet.Value == 0)
            {
                DefenseBreakPercentNet.Value = 0;
                DefenseNet.Value = baseCombatDefense;
                if (cardData != null)
                    cardData.def = baseCombatDefense;
            }
        }
        if (TauntRoundsRemainingNet.Value > 0)
            TauntRoundsRemainingNet.Value--;
        if (StrengthBuffRoundsRemainingNet.Value > 0)
        {
            StrengthBuffRoundsRemainingNet.Value--;
            if (StrengthBuffRoundsRemainingNet.Value == 0)
            {
                StrengthBuffStacksNet.Value = 0;
                StrengthBuffPercentPerStackNet.Value = 0;
                RefreshAttackStatServerSide();
            }
        }
        if (InvisibleRoundsRemainingNet.Value > 0)
        {
            InvisibleRoundsRemainingNet.Value--;
            if (InvisibleRoundsRemainingNet.Value == 0)
                InvisibleBreaksOnAttackNet.Value = false;
        }

        TickDamageOverTime(BurnDamagePerTickNet, BurnRoundsRemainingNet);
        TickDamageOverTime(BleedDamagePerTickNet, BleedRoundsRemainingNet);
        TickDamageOverTime(PoisonDamagePerTickNet, PoisonRoundsRemainingNet);
    }

    private void TickDamageOverTime(
        NetworkVariable<int> damagePerTick,
        NetworkVariable<int> roundsRemaining)
    {
        if (roundsRemaining.Value <= 0)
            return;

        if (damagePerTick.Value > 0 && IsAlive)
            V156DamageResolver.ApplyDamage(null, this, damagePerTick.Value, false, true, false);

        roundsRemaining.Value = Mathf.Max(0, roundsRemaining.Value - 1);
        if (roundsRemaining.Value == 0)
            damagePerTick.Value = 0;
    }

    public void ApplyDotServerSide(V156DotKind kind, int damagePerTick, int rounds)
    {
        if (!IsServer || !IsAlive || damagePerTick <= 0 || rounds <= 0)
            return;

        NetworkVariable<int> damageNet;
        NetworkVariable<int> roundsNet;
        switch (kind)
        {
            case V156DotKind.Bleed:
                damageNet = BleedDamagePerTickNet;
                roundsNet = BleedRoundsRemainingNet;
                break;
            case V156DotKind.Poison:
                damageNet = PoisonDamagePerTickNet;
                roundsNet = PoisonRoundsRemainingNet;
                break;
            default:
                damageNet = BurnDamagePerTickNet;
                roundsNet = BurnRoundsRemainingNet;
                break;
        }

        // Aynı DOT türünün yenilenmesi süresini ve tik hasarını en az mevcut
        // seviyede tutar; farklı DOT türleri ayrı ayrı işlenir.
        damageNet.Value = Mathf.Max(damageNet.Value, damagePerTick);
        roundsNet.Value = Mathf.Max(roundsNet.Value, rounds);
    }

    private int GetAdjustedCcDuration(int baseRounds)
    {
        if (baseRounds <= 0)
            return 0;

        IPassiveAbility passive = !string.IsNullOrWhiteSpace(cardData?.passive)
            ? AbilityCatalog.GetPassive(cardData.passive)
            : null;
        if (passive is IV156CcDurationModifier modifier)
            return Mathf.Max(0, modifier.ModifyCcDuration(this, baseRounds));
        return baseRounds;
    }

    public void ApplyBlindServerSide(int rounds)
    {
        if (!IsServer || !IsAlive)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds <= 0)
            return;

        BlindRoundsRemainingNet.Value = Mathf.Max(BlindRoundsRemainingNet.Value, rounds);
        Debug.Log($"[CC_BLIND] {cardData?.cardName}: {BlindRoundsRemainingNet.Value} global raund.");
    }

    public void ApplyFearServerSide(int rounds)
    {
        if (!IsServer || !IsAlive)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds > 0)
            FearRoundsRemainingNet.Value = Mathf.Max(FearRoundsRemainingNet.Value, rounds);
    }

    public void ApplyMarkServerSide(int rounds)
    {
        if (!IsServer || !IsAlive)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds <= 0)
            return;

        MarkRoundsRemainingNet.Value = Mathf.Max(MarkRoundsRemainingNet.Value, rounds);
        DamageTakenMultiplierNet.Value = 1.20f;
    }

    public void ApplyDefenseBreakServerSide(int percent, int rounds)
    {
        if (!IsServer || !IsAlive || percent <= 0)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds <= 0)
            return;

        DefenseBreakPercentNet.Value = Mathf.Max(DefenseBreakPercentNet.Value, Mathf.Clamp(percent, 0, 100));
        DefenseBreakRoundsRemainingNet.Value = Mathf.Max(DefenseBreakRoundsRemainingNet.Value, rounds);
        int reducedDefense = Mathf.Max(0, Mathf.RoundToInt(baseCombatDefense *
            (1f - DefenseBreakPercentNet.Value / 100f)));
        DefenseNet.Value = reducedDefense;
        if (cardData != null)
            cardData.def = reducedDefense;
    }

    public void ApplyInvisibleServerSide(int rounds, bool breaksOnOwnAttack = true)
    {
        if (!IsServer || !IsAlive)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds <= 0)
            return;

        InvisibleRoundsRemainingNet.Value = Mathf.Max(InvisibleRoundsRemainingNet.Value, rounds);
        InvisibleBreaksOnAttackNet.Value |= breaksOnOwnAttack;
    }

    public void BreakInvisibilityOnOwnAttackServerSide()
    {
        if (!IsServer || !InvisibleBreaksOnAttackNet.Value)
            return;

        InvisibleRoundsRemainingNet.Value = 0;
        InvisibleBreaksOnAttackNet.Value = false;
    }

    /// <summary>
    /// CC_SILENCE is an immediate delay, not a timed status. It adds four ticks
    /// to the active unlock threshold before unlock, or to the current cooldown
    /// afterward; accumulated delay is capped at eight ticks per stage/cooldown.
    /// </summary>
    public void ApplySilenceServerSide(int ticks = 4)
    {
        if (!IsServer || ticks <= 0 || !IsAlive)
            return;

        const int baseUnlockThreshold = 4;
        const int maxAccumulatedPenalty = 8;
        int applied = Mathf.Clamp(ticks, 0, 4);

        if (PersonalCounterNet.Value < ActiveUnlockThresholdNet.Value)
        {
            int unlockPenalty = Mathf.Clamp(
                ActiveUnlockThresholdNet.Value - baseUnlockThreshold,
                0,
                maxAccumulatedPenalty);
            int permitted = Mathf.Min(applied, maxAccumulatedPenalty - unlockPenalty);
            ActiveUnlockThresholdNet.Value += permitted;

            Debug.Log($"[CC_SILENCE] {cardData?.cardName}: aktif açılış eşiği " +
                      $"{ActiveUnlockThresholdNet.Value} kişisel tike çıktı.");
            return;
        }

        int cooldownPenalty = Mathf.Clamp(ActiveCdSilencePenaltyNet.Value, 0, maxAccumulatedPenalty);
        int additionalPenalty = Mathf.Min(applied, maxAccumulatedPenalty - cooldownPenalty);
        if (additionalPenalty <= 0)
            return;

        AbilityCooldownRemaining.Value = Mathf.Max(AbilityCooldownRemaining.Value, 0) + additionalPenalty;
        ActiveCdSilencePenaltyNet.Value = cooldownPenalty + additionalPenalty;

        Debug.Log($"[CC_SILENCE] {cardData?.cardName}: aktif CD +{additionalPenalty}; " +
                  $"CD={AbilityCooldownRemaining.Value} (ceza tavanı +8).");
    }

    public void ArmAlevOzuServerSide()
    {
        if (IsServer)
            AlevOzuReadyNet.Value = true;
    }

    public bool ConsumeAlevOzuChargeServerSide()
    {
        if (!IsServer || !AlevOzuReadyNet.Value)
            return false;
        AlevOzuReadyNet.Value = false;
        AlevOzuPendingBurnNet.Value = true;
        return true;
    }

    public bool ConsumeAlevOzuPendingBurnServerSide()
    {
        if (!IsServer || !AlevOzuPendingBurnNet.Value)
            return false;
        AlevOzuPendingBurnNet.Value = false;
        return true;
    }

    public void AddRuneServerSide(int count = 1)
    {
        if (IsServer && count > 0)
            RuneStacksNet.Value += count;
    }

    public bool ConsumeThreeRunesServerSide()
    {
        if (!IsServer || RuneStacksNet.Value < 3)
            return false;
        RuneStacksNet.Value = 0;
        return true;
    }

    public void StoreAbilityDamageBonusServerSide(int amount)
    {
        if (IsServer)
            StoredAbilityDamageBonusNet.Value = Mathf.Max(0, amount);
    }

    public int ConsumeStoredAbilityDamageBonusServerSide()
    {
        if (!IsServer)
            return 0;
        int bonus = StoredAbilityDamageBonusNet.Value;
        StoredAbilityDamageBonusNet.Value = 0;
        return bonus;
    }

    public void AddStaticChargeServerSide(int amount)
    {
        if (IsServer && amount > 0)
            StaticChargeDamageNet.Value += amount;
    }

    public int ConsumeStaticChargeServerSide()
    {
        if (!IsServer)
            return 0;
        int charge = StaticChargeDamageNet.Value;
        StaticChargeDamageNet.Value = 0;
        return charge;
    }

    public void ApplyStrengthBuffServerSide(int percentPerStack, int duration, int maxStacks)
    {
        if (!IsServer || percentPerStack <= 0 || duration <= 0 || maxStacks <= 0)
            return;

        StrengthBuffPercentPerStackNet.Value = percentPerStack;
        StrengthBuffStacksNet.Value = Mathf.Min(maxStacks, StrengthBuffStacksNet.Value + 1);
        StrengthBuffRoundsRemainingNet.Value = Mathf.Max(StrengthBuffRoundsRemainingNet.Value, duration);
        RefreshAttackStatServerSide();
    }

    public void ApplyPermanentStrengthPercentServerSide(int percent)
    {
        if (!IsServer || percent <= 0)
            return;

        baseCombatAttack = Mathf.FloorToInt(baseCombatAttack * (1f + percent / 100f) + 0.5f);
        RefreshAttackStatServerSide();
    }

    private void RefreshAttackStatServerSide()
    {
        if (!IsServer)
            return;

        float multiplier = 1f + StrengthBuffPercentPerStackNet.Value *
            StrengthBuffStacksNet.Value / 100f;
        int attack = Mathf.Max(0, Mathf.FloorToInt(baseCombatAttack * multiplier + 0.5f));
        AttackNet.Value = attack;
        if (cardData != null)
            cardData.str = attack;
    }

    public void ReduceActiveCooldownServerSide(int ticks)
    {
        if (!IsServer || ticks <= 0 || AbilityCooldownRemaining.Value <= 0)
            return;

        AbilityCooldownRemaining.Value = Mathf.Max(0, AbilityCooldownRemaining.Value - ticks);
        if (AbilityCooldownRemaining.Value == 0)
            ActiveCdSilencePenaltyNet.Value = 0;
    }

    public void ApplyTauntServerSide(int rounds)
    {
        if (!IsServer || !IsAlive)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds > 0)
            TauntRoundsRemainingNet.Value = Mathf.Max(TauntRoundsRemainingNet.Value, rounds);
    }

    public void ApplyStunServerSide(int rounds)
    {
        if (!IsServer || !IsAlive)
            return;

        rounds = GetAdjustedCcDuration(rounds);
        if (rounds > 0)
            StunRoundsRemainingNet.Value = Mathf.Max(StunRoundsRemainingNet.Value, rounds);
    }

    #endregion

    // -----------------------------------------------------------------
    #region Ability Context / RPC

    private void InitAbilityStateServer()
    {
        if (!IsServer)
            return;

        PersonalCounterNet.Value = 0;
        ActiveUnlockThresholdNet.Value = 4;
        AbilityCooldownRemaining.Value = 4;
        ActiveCdSilencePenaltyNet.Value = 0;
        RuneStacksNet.Value = 0;
        AlevOzuReadyNet.Value = false;
        AlevOzuPendingBurnNet.Value = false;
        StoredAbilityDamageBonusNet.Value = 0;
        StaticChargeDamageNet.Value = 0;
        StrengthBuffStacksNet.Value = 0;
        StrengthBuffPercentPerStackNet.Value = 0;
        StrengthBuffRoundsRemainingNet.Value = 0;
        BlindRoundsRemainingNet.Value = 0;
        FearRoundsRemainingNet.Value = 0;
        MarkRoundsRemainingNet.Value = 0;
        DamageTakenMultiplierNet.Value = 1f;
        DefenseBreakRoundsRemainingNet.Value = 0;
        DefenseBreakPercentNet.Value = 0;
        TauntRoundsRemainingNet.Value = 0;
        BurnDamagePerTickNet.Value = 0;
        BurnRoundsRemainingNet.Value = 0;
        BleedDamagePerTickNet.Value = 0;
        BleedRoundsRemainingNet.Value = 0;
        PoisonDamagePerTickNet.Value = 0;
        PoisonRoundsRemainingNet.Value = 0;
        InvisibleRoundsRemainingNet.Value = 0;
        InvisibleBreaksOnAttackNet.Value = false;
        StunRoundsRemainingNet.Value = 0;
        LockActionThisRoundNet.Value = false;

        if (!string.IsNullOrWhiteSpace(cardData?.passive))
        {
            IPassiveAbility passive = AbilityCatalog.GetPassive(cardData.passive);
            passive?.OnSpawn(BuildContext(
                null,
                BattleManager.Instance != null ? BattleManager.Instance.CurrentRound : 1));
        }
    }

    private AbilityContext BuildContext(Character primaryTarget, int currentRound)
    {
        BattleGrid ownGrid = BattleManager.Instance != null
            ? BattleManager.Instance.GetGridFor(isPlayer)
            : null;
        BattleGrid enemyGrid = BattleManager.Instance != null
            ? BattleManager.Instance.GetGridFor(!isPlayer)
            : null;

        return new AbilityContext
        {
            Source = this,
            PrimaryTarget = primaryTarget,
            AllTargets = primaryTarget != null
                ? new System.Collections.Generic.List<Character> { primaryTarget }
                : new System.Collections.Generic.List<Character>(),
            OwnGrid = ownGrid,
            EnemyGrid = enemyGrid,
            CurrentRound = currentRound
        };
    }

    [ServerRpc(RequireOwnership = false)]
    public void UseActiveAbilityServerRpc(int targetColumn, int targetRow, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        BattleManager manager = BattleManager.Instance;
        if (manager == null)
            return;

        AbilityTargetSide side = ActiveAbilityTargetSide;
        BattleGrid targetGrid = manager.GetGridFor(side == AbilityTargetSide.Ally ? isPlayer : !isPlayer);
        Character target = side == AbilityTargetSide.Self
            ? this
            : targetGrid != null && targetColumn >= 0 && targetColumn < targetGrid.Columns
                ? targetGrid.GetOccupant((BattleRow)Mathf.Clamp(targetRow, 0, 2), targetColumn)
                : null;

        ulong[] targetIds = target != null && target != this
            ? new[] { target.NetworkObjectId }
            : System.Array.Empty<ulong>();
        ExecuteV156ActiveAbilityServerSide(targetIds, senderId);
    }

    /// <summary>
    /// Authoritative V1.56 active cast endpoint. targetNetworkObjectIds is a client
    /// selection only; ownership, target count, side, row, visibility and readiness
    /// are all checked again on the server.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void UseActiveAbilityTargetsServerRpc(
        ulong[] targetNetworkObjectIds,
        ServerRpcParams rpcParams = default)
    {
        ExecuteV156ActiveAbilityServerSide(
            targetNetworkObjectIds ?? System.Array.Empty<ulong>(),
            rpcParams.Receive.SenderClientId);
    }

    private static readonly System.Collections.Generic.Dictionary<ulong, int> lastV156ActiveRoundByClient =
        new System.Collections.Generic.Dictionary<ulong, int>();

    private void ExecuteV156ActiveAbilityServerSide(ulong[] targetIds, ulong senderId)
    {
        BattleManager manager = BattleManager.Instance;
        if (!IsServer || manager == null)
            return;

        if (manager.Phase.Value != BattlePhase.PlayerTurn ||
            manager.ActivePlayerClientId.Value != senderId || !isMyTurn ||
            !manager.IsSenderAuthorizedForCharacter(senderId, this))
        {
            manager.WarnClient(senderId, "Şu anda bu karakterle yetenek kullanamazsın.");
            return;
        }

        if (!IsAlive)
        {
            manager.WarnClient(senderId, "Karakter düşmüş durumda.");
            return;
        }

        if (IsStunned)
        {
            manager.WarnClient(senderId, "Karakter sersemletildi; aksiyon boşa harcandı.");
            manager.EndTurn();
            return;
        }

        string reason = null;
        if (!IsActiveAbilityReady)
            reason = BattleManager.DescribeAbilityNotReady(this);
        else if (lastV156ActiveRoundByClient.TryGetValue(senderId, out int usedRound) &&
                 usedRound == manager.CurrentRound)
            reason = "Bu raundda zaten bir aktif yetenek kullandınız.";

        if (!string.IsNullOrEmpty(reason))
        {
            manager.WarnClient(senderId, reason);
            return;
        }

        if (string.IsNullOrWhiteSpace(cardData?.ability))
        {
            manager.WarnClient(senderId, "Bu kartın aktif yeteneği tanımlı değil.");
            return;
        }

        IActiveAbility ability = AbilityCatalog.GetActive(cardData.ability);
        if (ability == null)
        {
            manager.WarnClient(senderId, $"'{cardData.ability}' kimlikli yetenek kayıtlı değil.");
            return;
        }

        if (!TryResolveV156Targets(ability, targetIds, manager, out var targets, out reason))
        {
            manager.WarnClient(senderId, reason);
            return;
        }

        var context = BuildMultiTargetContext(targets, manager.CurrentRound);
        ability.Apply(context);

        lastV156ActiveRoundByClient[senderId] = manager.CurrentRound;
        AbilityCooldownRemaining.Value = 4;
        ActiveCdSilencePenaltyNet.Value = 0;
        PlayAnimationClientRpc("Skill");
        ulong visualTargetId = targets.Count > 0 && targets[0] != null
            ? targets[0].NetworkObjectId
            : NetworkObjectId;
        PlayV156AbilityFeedbackClientRpc(
            cardData.ability,
            visualTargetId,
            ability is IV156SuppressVfx);
        manager.EndTurn();
    }

    [ClientRpc]
    private void PlayV156AbilityFeedbackClientRpc(
        string abilityId,
        ulong targetNetworkObjectId,
        bool suppressVfx)
    {
        if (suppressVfx)
            return;

        Character target = FindSpawnedCharacter(targetNetworkObjectId);
        Vector3 sourcePosition = transform.position;
        Vector3 targetPosition = target != null ? target.transform.position : sourcePosition;
        bool playProjectile = target != null && target != this;

        if (VFXManager.Instance != null)
        {
            string sourceKey = GetV156CharacterVfxKey();
            VFXManager.Instance.PlayVFX(sourceKey + "_Cast", sourcePosition);
            if (playProjectile)
                VFXManager.Instance.PlayProjectile(sourceKey + "_Projectile", sourcePosition, targetPosition, 35f);
            VFXManager.Instance.PlayVFX(sourceKey + "_Impact", targetPosition);
        }

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(abilityId))
            AudioManager.Instance.PlaySFX(abilityId);
    }

    private string GetV156CharacterVfxKey()
    {
        if (cardData != null)
        {
            if (cardData.characterPrefab3D != null)
                return cardData.characterPrefab3D.name.Replace("(Clone)", "").Trim();
            if (!string.IsNullOrWhiteSpace(cardData.prefab))
            {
                GameObject resourcePrefab = Resources.Load<GameObject>(cardData.prefab);
                if (resourcePrefab != null)
                    return resourcePrefab.name.Replace("(Clone)", "").Trim();
            }
        }
        return gameObject.name.Replace("(Clone)", "").Trim();
    }

    private bool TryResolveV156Targets(
        IActiveAbility ability,
        ulong[] targetIds,
        BattleManager manager,
        out System.Collections.Generic.List<Character> targets,
        out string reason)
    {
        targets = new System.Collections.Generic.List<Character>();
        reason = null;

        V156TargetRule rule = ResolveV156TargetRule(ability);
        int required = ability is IV156TargetedAbility metadata
            ? Mathf.Max(1, metadata.RequiredTargets)
            : 1;
        AbilityTargetSide side = ability is IAbilityTargetSide sided
            ? sided.TargetSide
            : rule == V156TargetRule.Self
                ? AbilityTargetSide.Self
                : rule == V156TargetRule.SingleAlly || rule == V156TargetRule.LowestHpAlly ||
                  rule == V156TargetRule.AllAllies
                    ? AbilityTargetSide.Ally
                    : AbilityTargetSide.Enemy;

        BattleGrid ownGrid = manager.GetGridFor(isPlayer);
        BattleGrid enemyGrid = manager.GetGridFor(!isPlayer);

        if (rule == V156TargetRule.Self || side == AbilityTargetSide.Self)
        {
            targets.Add(this);
            return true;
        }

        if (rule == V156TargetRule.AllEnemies)
        {
            if (enemyGrid != null)
                targets.AddRange(enemyGrid.GetAllAlive()); // AoE görünmezleri de vurur.
            if (targets.Count == 0)
                reason = "Hedef alınabilecek canlı düşman yok.";
            return targets.Count > 0;
        }

        if (rule == V156TargetRule.AllAllies)
        {
            if (ownGrid != null)
                targets.AddRange(ownGrid.GetAllAlive());
            if (targets.Count == 0)
                reason = "Takımında canlı hedef yok.";
            return targets.Count > 0;
        }

        if (rule == V156TargetRule.LowestHpAlly)
        {
            if (ownGrid != null)
            {
                Character lowest = GetLowestHpTarget(ownGrid.GetAllAlive());
                if (lowest != null)
                    targets.Add(lowest);
            }
            if (targets.Count == 0)
                reason = "Takımında canlı hedef yok.";
            return targets.Count > 0;
        }

        bool allyTarget = side == AbilityTargetSide.Ally || rule == V156TargetRule.SingleAlly;
        BattleGrid targetGrid = allyTarget ? ownGrid : enemyGrid;
        if (targetGrid == null)
        {
            reason = "Saha hazır değil.";
            return false;
        }

        if (rule == V156TargetRule.MultiEnemy)
        {
            var validFrontTargets = new System.Collections.Generic.List<Character>();
            foreach (Character candidate in targetGrid.GetAllAlive())
                if (candidate.gridRow == BattleRow.Front && !candidate.IsInvisible)
                    validFrontTargets.Add(candidate);
            SortTargetsByCurrentHp(validFrontTargets);

            if (validFrontTargets.Count == 0)
            {
                reason = "Ön hatta seçilebilir canlı düşman yok.";
                return false;
            }

            var selected = new System.Collections.Generic.List<Character>();
            var selectedIds = new System.Collections.Generic.HashSet<ulong>();
            foreach (ulong id in targetIds)
            {
                if (!selectedIds.Add(id))
                {
                    reason = "Aynı hedef birden fazla seçilemez.";
                    return false;
                }

                Character candidate = FindSpawnedCharacter(id);
                if (candidate == null || !candidate.IsAlive || candidate.isPlayer == isPlayer ||
                    candidate.gridRow != BattleRow.Front || candidate.IsInvisible ||
                    !validFrontTargets.Contains(candidate))
                {
                    reason = "Çoklu seçimde geçersiz, görünmez veya ön hat dışında hedef var.";
                    return false;
                }
                selected.Add(candidate);
            }

            if (selected.Count > required)
            {
                reason = $"Bu yetenek en fazla {required} hedef seçebilir.";
                return false;
            }

            int needed = Mathf.Min(required, validFrontTargets.Count);
            if (selected.Count < needed && validFrontTargets.Count >= required)
            {
                reason = $"{required} farklı hedef seç veya Oto Doldur'u kullan.";
                return false;
            }

            // Geçerli hedef sayısı gereken sayıdan azsa kalan hücreleri sunucu,
            // mevcut HP'si en düşük ve henüz seçilmemiş hedeflerle tamamlar.
            foreach (Character candidate in validFrontTargets)
            {
                if (selected.Count >= needed)
                    break;
                if (!selected.Contains(candidate))
                    selected.Add(candidate);
            }

            targets.AddRange(selected);
            return targets.Count > 0;
        }

        if (targetIds == null || targetIds.Length != 1)
        {
            reason = allyTarget
                ? "Tek bir takım arkadaşı seçmelisin."
                : "Tek bir düşman hedefi seçmelisin.";
            return false;
        }

        Character single = FindSpawnedCharacter(targetIds[0]);
        if (single == null || !single.IsAlive ||
            (allyTarget ? single.isPlayer != isPlayer : single.isPlayer == isPlayer) ||
            targetGrid.GetOccupant(single.gridRow, single.gridColumn) != single)
        {
            reason = "Seçilen hedef geçersiz veya düşmüş.";
            return false;
        }

        if (!allyTarget && single.IsInvisible)
        {
            reason = "Görünmez karakter tekil hedef seçilemez.";
            return false;
        }

        if (!allyTarget && ability.TargetingMode != AbilityTargetingMode.AnyTarget &&
            !IsLegalSingleEnemyTarget(targetGrid, single, ability.TargetingMode))
        {
            reason = "Seçilen düşman ön hat kuralına göre hedeflenemiyor.";
            return false;
        }

        targets.Add(single);
        return true;
    }

    private static V156TargetRule ResolveV156TargetRule(IActiveAbility ability)
    {
        if (ability is IV156TargetedAbility metadata)
            return metadata.TargetRule;
        if (ability is IAbilityTargetSide sided)
        {
            if (sided.TargetSide == AbilityTargetSide.Self)
                return V156TargetRule.Self;
            if (sided.TargetSide == AbilityTargetSide.Ally)
                return V156TargetRule.SingleAlly;
        }
        return V156TargetRule.SingleEnemy;
    }

    private bool IsLegalSingleEnemyTarget(
        BattleGrid grid,
        Character target,
        AbilityTargetingMode mode)
    {
        if (grid == null || target == null)
            return false;

        var order = mode == AbilityTargetingMode.IgnoreFrontline
            ? new[] { BattleRow.Back, BattleRow.Mid, BattleRow.Front }
            : new[] { BattleRow.Front, BattleRow.Mid, BattleRow.Back };

        foreach (BattleRow row in order)
        {
            Character occupant = grid.GetOccupant(row, target.gridColumn);
            if (occupant != null && occupant.IsAlive && !occupant.IsInvisible)
                return occupant == target;
        }

        return false;
    }

    private static Character FindSpawnedCharacter(ulong networkObjectId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                networkObjectId, out NetworkObject netObject))
            return null;
        return netObject.GetComponent<Character>();
    }

    private static Character GetLowestHpTarget(System.Collections.Generic.List<Character> characters)
    {
        if (characters == null || characters.Count == 0)
            return null;
        SortTargetsByCurrentHp(characters);
        return characters[0];
    }

    private static void SortTargetsByCurrentHp(System.Collections.Generic.List<Character> characters)
    {
        characters.Sort((a, b) =>
        {
            int hpA = GetCurrentHp(a);
            int hpB = GetCurrentHp(b);
            int hpOrder = hpA.CompareTo(hpB);
            if (hpOrder != 0) return hpOrder;
            int rowOrder = ((int)a.gridRow).CompareTo((int)b.gridRow);
            return rowOrder != 0 ? rowOrder : a.gridColumn.CompareTo(b.gridColumn);
        });
    }

    private static int GetCurrentHp(Character character)
    {
        HealthSystem targetHealth = character != null ? character.GetComponent<HealthSystem>() : null;
        return targetHealth != null ? targetHealth.currentHP.Value : int.MaxValue;
    }

    private AbilityContext BuildMultiTargetContext(
        System.Collections.Generic.List<Character> targets,
        int currentRound)
    {
        BattleManager manager = BattleManager.Instance;
        Character primary = targets != null && targets.Count > 0 ? targets[0] : null;
        return new AbilityContext
        {
            Source = this,
            PrimaryTarget = primary,
            AllTargets = targets ?? new System.Collections.Generic.List<Character>(),
            OwnGrid = manager != null ? manager.GetGridFor(isPlayer) : null,
            EnemyGrid = manager != null ? manager.GetGridFor(!isPlayer) : null,
            CurrentRound = currentRound
        };
    }

    #endregion

    // -----------------------------------------------------------------
    #region Setup / Turn / Team

    public void Setup(CardData data, ulong controllerClientId = ulong.MaxValue)
    {
        cardData = data;

        if (health == null)
            health = GetComponent<HealthSystem>();

        if (health != null)
        {
            health.maxHP = data.baseHP;
            if (IsServer)
            {
                health.currentHP.Value = data.baseHP;
                health.shield = 0;
            }
        }

        maxHp = data.baseHP;
        baseCombatAttack = data.str > 0 ? data.str : data.baseStr;
        baseCombatDefense = data.def > 0 ? data.def : data.baseDef;
        defense = baseCombatDefense;
        name = data.cardName;

        if (IsServer)
        {
            ControllerClientIdNet.Value = controllerClientId;
            DisplayNameNet.Value = new FixedString64Bytes(
                string.IsNullOrWhiteSpace(data.cardName) ? "Karakter" : data.cardName);
            MaxHpNet.Value = data.baseHP;
            AttackNet.Value = data.str > 0 ? data.str : data.baseStr;
            DefenseNet.Value = data.def > 0 ? data.def : data.baseDef;

            IActiveAbility activeAbility = string.IsNullOrWhiteSpace(data.ability)
                ? null
                : AbilityCatalog.GetActive(data.ability);
            AbilityTargetSideNet.Value = (int)(activeAbility is IAbilityTargetSide sided
                ? sided.TargetSide
                : AbilityTargetSide.Enemy);

            // Setup Round 0'daki yerleşimdeyse Round 1 tikini bekler.
            // Savaş ortasında spawn olduysa mevcut raundun tikini tekrar almaz.
            if (lastProcessedRound < 0 && BattleManager.Instance != null)
                lastProcessedRound = BattleManager.Instance.RoundNumber.Value;

            SubscribeToRoundTicks();
            InitAbilityStateServer();
        }
    }

    public bool IsControlledBy(ulong clientId) => ControllerClientIdNet.Value == clientId;

    public void SetTurn(bool turn)
    {
        if (IsServer)
            IsMyTurnNet.Value = turn;
    }

    public void SetPlayerSide(bool playerSide)
    {
        if (IsServer)
            IsPlayerNet.Value = playerSide;
    }

    [ServerRpc(RequireOwnership = false)]
    public void AttackSelectedTargetServerRpc(
        ulong targetNetworkObjectId,
        ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        BattleManager manager = BattleManager.Instance;
        if (!IsServer || manager == null)
            return;

        if (manager.Phase.Value != BattlePhase.PlayerTurn ||
            manager.ActivePlayerClientId.Value != senderId || !isMyTurn ||
            !manager.IsSenderAuthorizedForCharacter(senderId, this))
        {
            manager.WarnClient(senderId, "Saldırı isteği yetkisiz veya sıra dışı.");
            return;
        }

        if (!IsAlive)
        {
            manager.WarnClient(senderId, "Karakter düşmüş durumda.");
            return;
        }
        if (IsStunned)
        {
            manager.WarnClient(senderId, "Karakter sersemletildi; aksiyon boşa harcandı.");
            manager.EndTurn();
            return;
        }
        if (IsFeared)
        {
            manager.WarnClient(senderId, "Korku etkisi: normal saldırı kullanılamaz.");
            return;
        }

        Character target = FindSpawnedCharacter(targetNetworkObjectId);
        BattleGrid enemyGrid = manager.GetGridFor(!isPlayer);
        if (target == null || !target.IsAlive || target.isPlayer == isPlayer)
        {
            manager.WarnClient(senderId, "Geçersiz veya düşmüş düşman hedefi.");
            return;
        }

        Character taunter = enemyGrid != null
            ? enemyGrid.GetAllAlive().Find(candidate => candidate.IsTaunting)
            : null;
        if (taunter != null)
        {
            if (target != taunter)
            {
                manager.WarnClient(senderId, "Taunt etkisi: yalnızca taunt uygulayan karaktere saldırabilirsin.");
                return;
            }
        }
        else if (target.IsInvisible || !IsLegalSingleEnemyTarget(enemyGrid, target, AbilityTargetingMode.Normal))
        {
            manager.WarnClient(senderId, target.IsInvisible
                ? "Görünmez hedef tekil saldırıyla hedeflenemez."
                : "Seçilen hedef ön hat kuralına göre saldırılamıyor.");
            return;
        }

        HealthSystem targetHealth = target.GetComponent<HealthSystem>();
        if (targetHealth == null)
        {
            manager.WarnClient(senderId, "Hedefte HealthSystem bulunamadı.");
            return;
        }

        int attack = Mathf.Max(0, AttackNet.Value);
        int defenseValue = Mathf.Max(0, target.EffectiveDefense);
        int damage = Mathf.Max(1, attack - defenseValue);
        bool critical = Random.value < BattleRules.BaseCriticalChance;
        if (critical)
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * BattleRules.CriticalMultiplier));

        IPassiveAbility passive = !string.IsNullOrWhiteSpace(cardData?.passive)
            ? AbilityCatalog.GetPassive(cardData.passive)
            : null;
        AbilityContext context = BuildMultiTargetContext(
            new System.Collections.Generic.List<Character> { target },
            manager.CurrentRound);
        if (passive is IV156NormalAttackPassive attackPassive)
            attackPassive.BeforeNormalAttack(context, ref damage, ref critical);

        int applied = V156DamageResolver.ApplyDamage(this, target, damage, critical, false);
        if (passive is IV156NormalAttackPassive afterAttackPassive)
            afterAttackPassive.AfterNormalAttack(context, applied, critical);

        BreakInvisibilityOnOwnAttackServerSide();
        PlayAnimationClientRpc("Attack");
        PlayV156BasicAttackFeedbackClientRpc(target.NetworkObjectId, manager.basicAttackSfxId);
        manager.EndTurn();
    }

    [ClientRpc]
    private void PlayV156BasicAttackFeedbackClientRpc(ulong targetNetworkObjectId, string attackSfxId)
    {
        if (VFXManager.Instance != null)
            VFXManager.Instance.PlayCharacterVFX(GetV156CharacterVfxKey(), "Attack", transform.position);

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(attackSfxId))
            AudioManager.Instance.PlaySFX(attackSfxId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void AttackServerRpc(int targetColumn, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        BattleManager manager = BattleManager.Instance;
        if (manager == null)
            return;

        if (manager.Phase.Value != BattlePhase.PlayerTurn || !isMyTurn || !IsAlive || !CanUseNormalAction)
        {
            manager.WarnClient(senderId, "Şu anda bu karakterle saldıramazsın.");
            return;
        }

        if (!manager.IsSenderAuthorizedForCharacter(senderId, this))
        {
            Debug.LogWarning($"[CHAR] Yetkisiz AttackServerRpc reddedildi (client {senderId}) -> {cardData?.cardName}");
            manager.WarnClient(senderId, "Bu karakter üzerinde yetkin yok.");
            return;
        }

        if (manager.ResolveColumnAttack(this, targetColumn, AbilityTargetingMode.Normal))
            manager.EndTurn();
        else
            manager.WarnClient(senderId, $"Seçilen {targetColumn + 1}. sütunda geçerli hedef yok.");
    }

    #endregion
}
