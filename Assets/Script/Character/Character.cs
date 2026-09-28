using UnityEngine;
using Unity.Netcode;

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

    [Header("Optional")]
    public Animator animator;

    // -----------------------------------------------------------------
    #region Grid Konumu (6x3 saha sistemi)

    /// <summary>Bu karakterin sahasındaki sırası (Front/Mid/Back).</summary>
    public BattleRow gridRow { get; private set; }

    /// <summary>Bu karakterin sütunu. -1 = henüz bir hücreye yerleştirilmedi.</summary>
    public int gridColumn { get; private set; } = -1;

    public void SetGridPosition(BattleRow row, int column)
    {
        gridRow = row;
        gridColumn = column;
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
    public void UseActiveAbilityServerRpc(int targetColumn, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (BattleManager.Instance == null)
            return;

        if (!BattleManager.Instance.TryUseActiveAbilityForCharacter(this, targetColumn, senderId, out string reason))
        {
            Debug.LogWarning($"[CHAR] UseActiveAbilityServerRpc reddedildi ({cardData?.cardName}): {reason}");
        }
    }

    #endregion

    // -----------------------------------------------------------------

    // Setup çağrısı
    public void Setup(CardData data)
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

        InitAbilityStateServer();
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
        if (!isMyTurn) return;

        // Anti-cheat: RequireOwnership=false olduğu için herhangi bir client
        // bu RPC'yi HERHANGİ bir Character NetworkObject'i üzerinden
        // çağırabilir. isMyTurn tek başına yeterli değil - ayrıca gönderen
        // client'ın gerçekten BU karakterin tarafında olduğunu doğrula.
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (BattleManager.Instance == null ||
            !BattleManager.Instance.IsSenderAuthorizedForCharacter(senderId, this))
        {
            Debug.LogWarning($"[CHAR] Yetkisiz AttackServerRpc reddedildi (client {senderId}) -> {cardData?.cardName}");
            return;
        }

        BattleManager.Instance.ResolveColumnAttack(this, targetColumn, AbilityTargetingMode.Normal);
        BattleManager.Instance.EndTurn();
    }
}
