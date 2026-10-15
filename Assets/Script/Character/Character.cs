using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

[RequireComponent(typeof(NetworkObject))]
public class Character : NetworkBehaviour
{
    [Header("Stats")]
    public CardData cardData;
    public float maxHp;
    public float defense = 0f;

    private HealthSystem health;

    [Header("State")]
    public readonly NetworkVariable<bool> IsMyTurnNet =
    new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<bool> IsPlayerNet =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public bool isMyTurn => IsMyTurnNet.Value;
    public bool isPlayer => IsPlayerNet.Value;

    // NetworkObject ownership'i sunucuda kaldığı için oynanış sahipliğini ayrı
    // tutuyoruz. Böylece 2v2/3v3'te aynı taraftaki oyuncular yalnızca kendi
    // sahaya koydukları karakterlere komut verebilir.
    public readonly NetworkVariable<ulong> ControllerClientIdNet =
        new NetworkVariable<ulong>(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // CardData bir ScriptableObject/normal C# verisi olduğu için kendiliğinden
    // ağda çoğalmaz. Seçim bilgi panelinin her istemcide doğru değer göstermesi
    // için gerekli özet statlar ayrıca senkronlanır.
    public readonly NetworkVariable<FixedString64Bytes> DisplayNameNet =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> MaxHpNet =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> AttackNet =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> DefenseNet =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ulong ControllerClientId => ControllerClientIdNet.Value;

    // Aktif yeteneğin hedef tarafı (Enemy/Ally/Self). CardData istemcilerde
    // olmadığı için UI'nin hedef seçim akışını belirleyebilmesi adına senkronlanır.
    public readonly NetworkVariable<int> AbilityTargetSideNet =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
            Debug.LogError(
                $"[ANIM] {name}: Animator bulunamadı. Trigger={triggerName}"
            );
            return;
        }

        Debug.Log(
            $"[ANIM] {name}: Trigger gönderildi → {triggerName}, " +
            $"Controller={animator.runtimeAnimatorController?.name}"
        );

        animator.ResetTrigger(triggerName);
        animator.SetTrigger(triggerName);
    }

    private void Awake()
    {
        // Setup yalnız sunucuda çağrılabilir; istemciler de NetworkVariable HP
        // üzerinden IsAlive okuyabilsin diye component referansını yerelde kur.
        health = GetComponent<HealthSystem>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Can barı yalnızca görüntüleyen client'larda gerekir. Prefabların
        // tamamını tek tek değiştirmeden bütün karakterlere otomatik eklenir.
        if (IsClient && GetComponent<CharacterWorldHealthBar>() == null)
            gameObject.AddComponent<CharacterWorldHealthBar>();
    }

    // -----------------------------------------------------------------
    #region Grid Konumu (6x3 saha sistemi)

    /// <summary>Bu karakterin sahasındaki sırası (Front/Mid/Back).</summary>
    private readonly NetworkVariable<int> GridRowNet =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public BattleRow gridRow => (BattleRow)Mathf.Clamp(GridRowNet.Value, 0, 2);

    /// <summary>Bu karakterin sütunu. -1 = henüz bir hücreye yerleştirilmedi.</summary>
    private readonly NetworkVariable<int> GridColumnNet =
        new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int gridColumn => GridColumnNet.Value;

    public void SetGridPosition(BattleRow row, int column)
    {
        if (!IsServer)
            return;

        GridRowNet.Value = (int)row;
        GridColumnNet.Value = column;
    }

    #endregion

    // -----------------------------------------------------------------
    #region Yaşam Durumu

    /// <summary>
    /// HP > 0 ise canlıdır. Önceden 0 HP'ye düşen karakterler DESTROY
    /// ediliyordu (bkz. eski BattleManager.ResolveAttack / HealthSystem.Die).
    /// Tasarım dokümanındaki "yeniden dirilirse kalan süreyle devam eder"
    /// kuralı, karakterin öldükten sonra da sahnede (ve ability cooldown
    /// durumuyla) var olmaya devam etmesini gerektirir - bu yüzden artık
    /// öldürme, YOK ETME değil "düşürme" (downed) olarak ele alınıyor.
    /// Bkz. HealthSystem.Die().
    /// </summary>
    public bool IsAlive => health != null && health.currentHP.Value > 0;

    /// <summary>
    /// Karakter öldüğünde (HealthSystem.Die() tarafından) ya da dirildiğinde
    /// (Revive() tarafından) görsel/etkileşim durumunu günceller. Sadece
    /// sunucu çağırır; NetworkObject artık despawn EDİLMİYOR.
    /// </summary>
    public void SetDownedVisual(bool isDowned)
    {
        if (animator != null && isDowned)
            animator.SetTrigger("Die");

        // Düşmüş karakter hedef alınamasın diye collider'ları kapatıyoruz
        // (grid zaten IsAlive=false karakterleri hedeflemede atlıyor, bu
        // sadece fiziksel raycast/tıklama gibi client-taraflı etkileşimler
        // için ek bir görsel/işlevsel tutarlılık katmanı).
        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = !isDowned;
    }

    /// <summary>
    /// Karakteri, kalan ability cooldown'unu KORUYARAK yeniden ayağa
    /// kaldırır (bkz. AbilityCooldownRemaining - dirilme sırasında
    /// dokunulmuyor, zaten öldüğünde de sıfırlanmamıştı).
    /// Gerçek "revive" tetikleyicisi (hangi yeteneğin/olayın canlandıracağı)
    /// bu turun kapsamı dışında - bu sadece alt yapıyı hazırlar.
    /// </summary>
    public void Revive(int hpAmount)
    {
        if (!IsServer || health == null) return;

        health.currentHP.Value = Mathf.Clamp(hpAmount, 1, health.maxHP);
        health.shield = 0;
        SetDownedVisual(false);

        Debug.Log($"[CHAR] {cardData?.cardName} yeniden dirildi. Kalan yetenek bekleme: {AbilityCooldownRemaining.Value} raund.");
    }

    #endregion

    // -----------------------------------------------------------------
    #region Aktif Yetenek Cooldown Durumu
    //
    // Kural seti (tasarım dokümanı):
    //   - Karakter savaşa girdiğinde 3 raund sonra yeteneği hazırdır.
    //   - Kullanıldıktan sonra 4 raund bekler.
    //   - Karakter ölürse bekleme süresi sıfırlanmaz (dondurulur).
    //   - Yeniden dirilirse kalan süreyle devam eder.
    //   - Her oyuncu bir raundda yalnızca bir karakterin aktif yeteneğini
    //     kullanabilir (bkz. BattleManager.TryUseActiveAbilityForCharacter).
    //
    // Tek bir NetworkVariable<int> sayaç bu dört kuralın hepsini karşılar:
    // spawn'da 3'e set edilir, her raund başında (SADECE canlıyken) 1
    // azaltılır, kullanılınca 4'e set edilir. Ölüyken azaltılmadığı için
    // "sıfırlanmama/donma" otomatik sağlanır.
    public readonly NetworkVariable<int> AbilityCooldownRemaining = new NetworkVariable<int>(
        3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsActiveAbilityReady => AbilityCooldownRemaining.Value <= 0;

    private const int ActiveAbilityWarmupRounds = 3;
    private const int ActiveAbilityCooldownRounds = 4;

    /// <summary>Her yeni raund başında BattleManager tarafından çağrılır.</summary>
    public void OnRoundStartServer(int currentRound)
    {
        if (!IsServer) return;

        // Pasif yetenek: sürekli devrede, elle tetiklenmez.
        if (!string.IsNullOrEmpty(cardData?.passive))
        {
            var passive = AbilityCatalog.GetPassive(cardData.passive);
            if (passive != null && IsAlive)
            {
                passive.OnRoundStart(BuildContext(null, currentRound));
            }
        }

        // Aktif yetenek cooldown tik'i - SADECE canlıyken azalır (ölüyken donar).
        if (IsAlive && AbilityCooldownRemaining.Value > 0)
        {
            AbilityCooldownRemaining.Value--;
        }
    }

    /// <summary>Karakter sahaya girdiğinde (Setup içinden) çağrılır.</summary>
    private void InitAbilityStateServer()
    {
        if (!IsServer) return;

        AbilityCooldownRemaining.Value = ActiveAbilityWarmupRounds;

        if (!string.IsNullOrEmpty(cardData?.passive))
        {
            var passive = AbilityCatalog.GetPassive(cardData.passive);
            passive?.OnSpawn(BuildContext(null, BattleManager.Instance != null ? BattleManager.Instance.CurrentRound : 1));
        }
    }

    private AbilityContext BuildContext(Character primaryTarget, int currentRound)
    {
        BattleGrid ownGrid = BattleManager.Instance != null ? BattleManager.Instance.GetGridFor(isPlayer) : null;
        BattleGrid enemyGrid = BattleManager.Instance != null ? BattleManager.Instance.GetGridFor(!isPlayer) : null;

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

    /// <summary>
    /// Oyuncunun elle tetiklediği AKTİF yetenek çağrısı. Cooldown/raund-limiti
    /// muhasebesi burada DEĞİL, BattleManager.TryUseActiveAbilityForCharacter
    /// içinde yapılır (tüm oyuncunun karakterleri arasında "raunda 1 kullanım"
    /// kısıtını tek yerden takip edebilmek için).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void UseActiveAbilityServerRpc(int targetColumn, int targetRow, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        BattleManager manager = BattleManager.Instance;
        if (manager == null)
            return;

        if (!manager.TryUseActiveAbilityForCharacter(this, targetColumn, senderId, out string reason, targetRow))
        {
            Debug.LogWarning($"[CHAR] UseActiveAbilityServerRpc reddedildi ({cardData?.cardName}): {reason}");
            manager.WarnClient(senderId, reason);
        }
    }

    #endregion

    // -----------------------------------------------------------------

    // Setup çağrısı
    public void Setup(CardData data, ulong controllerClientId = ulong.MaxValue)
    {
        cardData = data;

        if (health == null) health = GetComponent<HealthSystem>();
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
        defense = data.baseDef;
        name = data.cardName;

        if (IsServer)
        {
            ControllerClientIdNet.Value = controllerClientId;
            DisplayNameNet.Value = new FixedString64Bytes(
                string.IsNullOrWhiteSpace(data.cardName) ? "Karakter" : data.cardName);
            MaxHpNet.Value = data.baseHP;
            AttackNet.Value = data.str > 0 ? data.str : data.baseStr;
            DefenseNet.Value = data.def > 0 ? data.def : data.baseDef;

            IActiveAbility activeAbility = string.IsNullOrEmpty(data.ability)
                ? null
                : AbilityCatalog.GetActive(data.ability);
            AbilityTargetSideNet.Value = (int)(activeAbility is IAbilityTargetSide sided
                ? sided.TargetSide
                : AbilityTargetSide.Enemy);
        }

        InitAbilityStateServer();
    }

    public bool IsControlledBy(ulong clientId)
    {
        return ControllerClientIdNet.Value == clientId;
    }

    public void SetTurn(bool turn)
    {
        if (!IsServer)
            return;

        IsMyTurnNet.Value = turn;
    }

    public void SetPlayerSide(bool playerSide)
    {
        if (!IsServer)
            return;

        IsPlayerNet.Value = playerSide;
    }

    [ServerRpc(RequireOwnership = false)]
    public void AttackServerRpc(int targetColumn, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        BattleManager manager = BattleManager.Instance;
        if (manager == null)
            return;

        // Faz + sıra + canlılık. Yerleşim fazında ya da sırası olmayan karakterle saldırı yok.
        if (manager.Phase.Value != BattlePhase.PlayerTurn || !isMyTurn || !IsAlive)
        {
            manager.WarnClient(senderId, "Şu anda bu karakterle saldıramazsın.");
            return;
        }

        // Anti-cheat: RequireOwnership=false olduğu için herhangi bir client
        // bu RPC'yi HERHANGİ bir Character NetworkObject'i üzerinden
        // çağırabilir. isMyTurn tek başına yeterli değil - ayrıca gönderen
        // client'ın gerçekten BU karakterin tarafında olduğunu doğrula.
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
}
