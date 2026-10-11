using TMPro;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using System.Text;
using SK2025.VFX;

public class BattleManager : NetworkBehaviour
{
    public static BattleManager Instance;

    [Header("Kart Prefabları ve Konumları")]
    public GameObject characterPrefab; // Varsayılan prefab (CardData.characterPrefab3D yoksa)
    public GameObject cardPrefab;      // Kart UI prefabı (elde/gösterimde)
    public Transform[] playerGridPositions;
    public Transform[] enemyGridPositions;

    [Header("Spawner Noktaları")]
    public Transform playerSpawnPoint;
    public Transform enemySpawnPoint;

    [Header("UI")]
    public Button attackButton;
    public Button skillButton;
    public Transform enemyCardParent;
    public GameObject cardSlotPrefab;

    [Header("Uyarı UI")]
    public TMP_Text warningText;

    [Header("Tur Sayacı UI")]
    [Tooltip("Boş bırakılırsa BattleScene açıldığında ekranın üstünde otomatik oluşturulur.")]
    [SerializeField] private TMP_Text turnTimerText;

    [Tooltip("İstersen sahneden bağla; bağlanmazsa da OnStartBattleButton() çağrılabilir.")]
    public Button startBattleButton;

    [Header("Tur / Faz Yönetimi")]
    public ulong currentTurnClientId;

    public readonly NetworkVariable<BattlePhase> Phase = new NetworkVariable<BattlePhase>(
        BattlePhase.WaitingForDecks,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<ulong> ActivePlayerClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<int> RoundNumber = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly HashSet<ulong> initialPlacementCompleted = new();
    private readonly HashSet<ulong> placedThisTurn = new();
    private readonly List<ulong> matchPlayers = new();
    private int activePlayerIndex;

    [Header("Saha Alanları")]
    public Collider playerZone;
    public Collider enemyZone;




    // --- RUNTIME ---
    private readonly List<Character> allCharacters = new();
    private readonly Dictionary<ulong, List<string>> playerSubmittedCardIds = new();
    private readonly Dictionary<ulong, List<CardData>> playerSubmittedCards = new();
    private readonly Dictionary<ulong, HashSet<string>> playerSubmittedJtis = new();
    private readonly Dictionary<ulong, int> expectedSubmittedCardCounts = new();
    private List<Character> turnOrder = new();
    private int currentIndex = 0;

    // Tek kart spawn için sıralı slot takibi
    private int nextPlayerSpawnIndex = 0;
    private int nextEnemySpawnIndex = 0;

    [Header("Geliştirme / Eski Kayıt Uyumluluğu")]
    [Tooltip("Yalnızca prototip testi için: signedToken taşımayan eski kart kayıtlarını kabul eder. Yayın sürümünde kapatılmalıdır.")]
    public bool allowUnsignedTestDeck = true;

    [Tooltip("Gerçek kart modeli bulunamazsa Inspector'daki eski varsayılan prefab yerine görünür test kapsülünü zorlar.")]
    public bool forceDebugCharacterVisuals = true;

    [Header("Sınırlar (Anti-Cheat)")]
    [Tooltip("Bir tarafın aynı anda sahada bulundurabileceği maksimum karakter sayısı.")]
    public int maxActiveCharactersPerSide = 12;

    // ---------------------------------------------------------------------
    #region Grid / Raund / AFK / Teslim Olma Durumu

    [Header("Saha Grid Ayarları")]
    [Tooltip("Taraf başına sütun sayısı. 1v1 için 6 (tasarım dokümanı). 2v2/3v3 için matchmaking henüz yok.")]
    public int gridColumns = 6;

    [Tooltip("SpawnCharacterAtPositionServerRpc ile serbest tıklamayla yerleştirmede, " +
             "sahnenizdeki 'ön hat' Z ekseninde min mi max mı tarafta? Test edip yanlışsa çevirin.")]
    public bool playerFrontRowIsMaxZ = true;
    public bool enemyFrontRowIsMaxZ = false;

    private BattleGrid playerGrid;
    private BattleGrid enemyGrid;

    public BattleGrid GetGridFor(bool isPlayerSide) => isPlayerSide ? playerGrid : enemyGrid;

    /// <summary>Şu anki raund numarası (1'den başlar). turnOrder tam bir tur tamamlayınca artar.</summary>
    public int CurrentRound => Mathf.Max(1, RoundNumber.Value);

    // Raund başına her gerçek oyuncu en fazla bir aktif yetenek kullanabilir.
    // 1v1 davranışı değişmez; 2v2/3v3'te takım arkadaşının yetenek kullanması
    // diğer oyuncunun hakkını tüketmez.
    private readonly Dictionary<ulong, int> lastAbilityRoundByClient = new();

    private BattleActionSelectionUI actionSelectionUI;

    [Header("AFK / Süre Aşımı")]
    [Tooltip("Bir karakterin turunu tamamlamak için verilen süre (saniye). 0 = sınırsız (devre dışı).")]
    public float turnTimeLimitSeconds = BattleRules.SelectionSeconds;

    [Tooltip("Art arda kaç kez süresi geçerse o taraf otomatik mağlup sayılır.")]
    public int maxConsecutiveAfkSkipsBeforeForfeit = 3;

    public readonly NetworkVariable<float> TurnTimeRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int playerConsecutiveAfkSkips = 0;
    private int enemyConsecutiveAfkSkips = 0;
    private bool battleEnded = false;

    // Teslim olma: 1v1'de tek oyluk "oylama" (kendi kendine teslim), takım
    // modları eklendiğinde requiredVotes takım boyutuna çıkarılabilir.
    private readonly HashSet<ulong> playerSurrenderVotes = new();
    private readonly HashSet<ulong> enemySurrenderVotes = new();

    #endregion
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    #region Yetki / Anti-Cheat Yardımcıları
    //
    // 2026 güvenlik incelemesi bulguları: SendAttackServerRpc,
    // SendSkillAttackServerRpc, PlayCardServerRpc ve
    // SpawnCharacterAtPositionServerRpc [ServerRpc] olarak sunucuda
    // çalışıyordu ama HİÇBİRİ gönderen client'ın (a) o an sırası olup
    // olmadığını, (b) oynatılan kartın gerçekten kendisine ait olup
    // olmadığını doğrulamıyordu. Bu, bir client'ın sırası rakipteyken bile
    // sınırsız saldırı/kart yerleştirme yapabilmesine yol açıyordu. Bu
    // bölümdeki yardımcılar tüm RPC'lerde ortak, tutarlı bir yetki
    // kontrolü sağlar.

    /// <summary>
    /// Gönderen client'ın hangi tarafta olduğunu (player/host tarafı mı,
    /// enemy/rakip tarafı mı) çözer. Önceden bu proje iki farklı ve
    /// birbiriyle tutarsız sezgisel yöntem kullanıyordu
    /// (ConnectedClientsList[0] ile NetworkManager.ServerClientId
    /// karşılaştırması ayrı ayrı yerlerde). Artık tek ve güvenilir kaynak:
    /// host her zaman NetworkManager.ServerClientId'dir; sunucuya bağlı
    /// host-dışı herhangi bir client rakip taraftır.
    /// </summary>
    private bool TryResolveSenderSide(ulong senderId, out bool isPlayerSide)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId))
        {
            isPlayerSide = LobbyTeamManager.IsClientOnPlayerSide(senderId);
            return true;
        }

        isPlayerSide = false;
        return false; // Bağlı olmayan/tanınmayan bir client - reddet.
    }

    /// <summary>Şu an aktif sıranın, verilen tarafa ait olup olmadığını kontrol eder.</summary>
    private bool IsSendersTurn(ulong senderId, out string reason)
    {
        reason = null;

        if (Phase.Value != BattlePhase.InitialPlacement && Phase.Value != BattlePhase.PlayerTurn)
        {
            reason = $"Bu fazda hamle yapılamaz: {Phase.Value}.";
            return false;
        }

        if (ActivePlayerClientId.Value != senderId)
        {
            reason = $"Sıra client {ActivePlayerClientId.Value} oyuncusunda.";
            return false;
        }

        return true;
    }

    /// <summary>Kartın, gönderen client'ın bu maç için gönderdiği desteye gerçekten ait olup olmadığını kontrol eder.</summary>
    private bool SenderOwnsCard(ulong senderId, CardData card)
    {
        if (card == null)
            return false;

        return FindSubmittedCardForSender(senderId, card.id) != null ||
               FindSubmittedCardForSender(senderId, card.cardId) != null ||
               FindSubmittedCardForSender(senderId, card.cardName) != null;
    }

    private CardData FindSubmittedCardForSender(ulong senderId, string key)
    {
        if (string.IsNullOrEmpty(key) ||
            !playerSubmittedCards.TryGetValue(senderId, out var cards) ||
            cards == null)
        {
            return null;
        }

        CardData exact = cards.FirstOrDefault(c =>
            c != null &&
            (string.Equals(c.id, key, System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.cardId, key, System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.cardName, key, System.StringComparison.OrdinalIgnoreCase)));

        if (exact != null)
            return exact;

        // Yerel kayıt CARD-Karakter-0000001, imzalı payload ise Karakter/CARD-Karakter
        // biçiminde olabiliyor. Oyuncuda aynı karakterden tek savaş kartı olduğu için
        // fiziksel seri sonunu kaldırarak güvenli biçimde kanonik eşleştirme yapıyoruz.
        string normalized = NormalizeCardKeyForMatch(key);
        CardData normalizedMatch = cards.FirstOrDefault(c =>
            c != null &&
            (NormalizeCardKeyForMatch(c.id) == normalized ||
             NormalizeCardKeyForMatch(c.cardId) == normalized ||
             NormalizeCardKeyForMatch(c.cardName) == normalized));

        if (normalizedMatch == null)
        {
            string submitted = string.Join(", ", cards.Where(c => c != null).Select(c => c.id));
            Debug.LogWarning($"[SECURE DECK] Client {senderId} kart eşleşmedi. İstenen='{key}', Gönderilen=[{submitted}]");
        }

        return normalizedMatch;
    }

    private static string NormalizeCardKeyForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string result = value.Trim().Replace("_", "-").Replace(" ", "-").ToLowerInvariant();
        if (result.StartsWith("card-")) result = result.Substring(5);

        string[] parts = result.Split('-');
        if (parts.Length > 1)
        {
            string last = parts[parts.Length - 1];
            bool numeric = last.All(char.IsDigit);
            if (numeric && last.Length >= 5)
                result = string.Join("-", parts.Take(parts.Length - 1));
        }

        return result.Replace("-", string.Empty);
    }

    /// <summary>Verilen tarafın sahada boş spawn slotu (kapasite) olup olmadığını kontrol eder.</summary>
    private bool HasFreeSpawnSlot(bool isPlayerSide)
    {
        int activeCount = allCharacters.Count(c => c != null && c.IsAlive && c.isPlayer == isPlayerSide);
        return activeCount < maxActiveCharactersPerSide;
    }

    /// <summary>
    /// Bir Character üzerinde (ör. Character.AttackServerRpc içinden)
    /// gönderen client'ın o karaktere komut verme yetkisi olup olmadığını
    /// kontrol etmek için kullanılır (kendi tarafının karakteri mi?).
    /// </summary>
    public bool IsSenderAuthorizedForCharacter(ulong senderId, Character character)
    {
        if (character == null) return false;
        if (!TryResolveSenderSide(senderId, out bool senderIsPlayerSide)) return false;

        // Yeni kayıtlar gerçek oyuncu sahipliği taşır. Eski sahne/prefab testleri
        // için ControllerClientId atanmamışsa taraf doğrulamasına geri düşer.
        if (character.ControllerClientId != ulong.MaxValue)
            return character.ControllerClientId == senderId;

        return character.isPlayer == senderIsPlayerSide;
    }

    public bool IsCharacterOnLocalPlayersTeam(Character character)
    {
        if (character == null || NetworkManager.Singleton == null)
            return false;

        bool localSide = LobbyTeamManager.IsClientOnPlayerSide(
            NetworkManager.Singleton.LocalClientId);
        return character.isPlayer == localSide;
    }

    public bool IsCharacterControlledByLocalPlayer(Character character)
    {
        if (character == null || NetworkManager.Singleton == null)
            return false;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        return character.ControllerClientId == ulong.MaxValue
            ? IsCharacterOnLocalPlayersTeam(character)
            : character.ControllerClientId == localId;
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Grid Kayıt / Sütun Bazlı Hedefleme / Aktif Yetenek Kullanımı

    private (BattleRow row, int column) RowColFromFlatIndex(int flatIndex)
    {
        int columns = Mathf.Max(1, gridColumns);
        int row = Mathf.Clamp(flatIndex / columns, 0, 2);
        int col = flatIndex % columns;
        return ((BattleRow)row, col);
    }

    /// <summary>
    /// Sabit-index tabanlı spawn akışları (SpawnCharacters, SpawnCharacter,
    /// SpawnRandomEnemyFromDeck) için: playerGridPositions/enemyGridPositions
    /// dizisindeki index'i satır/sütuna çevirip grid'e kaydeder.
    /// ÖNEMLİ: Bu diziler Inspector'da "row-major" sırayla (önce tüm Front
    /// sütunları, sonra Mid, sonra Back) düzenlenmiş OLMALI; 6x3 için 18
    /// eleman gerekir. Düzenlenmemişse grid mantıksal olarak yine çalışır
    /// (hücre çakışırsa TryOccupyNextFreeSlot'a düşer) ama görsel satır/sütun
    /// hizası bozuk olur.
    /// </summary>
    private void RegisterSpawnedCharacterByFlatIndex(Character ch, bool isPlayerSide, int flatIndex)
    {
        if (ch == null) return;

        var (row, col) = RowColFromFlatIndex(flatIndex);
        var grid = GetGridFor(isPlayerSide);

        if (grid != null && !grid.TryOccupy(ch, row, col))
            grid.TryOccupyNextFreeSlot(ch, out _, out _);

        allCharacters.Add(ch);
        turnOrder.Add(ch);
    }

    /// <summary>SpawnCharacterAtPositionServerRpc (serbest tıklama) için: dünya konumundan hücre çözer ve kaydeder.</summary>
    private (BattleRow row, int column) RegisterSpawnedCharacterByWorldPosition(Character ch, bool isPlayerSide, Bounds zoneBounds, Vector3 worldPos)
    {
        bool frontIsMaxZ = isPlayerSide ? playerFrontRowIsMaxZ : enemyFrontRowIsMaxZ;
        var (row, col) = BattleGrid.ResolveCellFromWorldPosition(zoneBounds, worldPos, gridColumns, frontIsMaxZ);

        var grid = GetGridFor(isPlayerSide);
        if (grid != null && !grid.TryOccupy(ch, row, col))
            grid.TryOccupyNextFreeSlot(ch, out row, out col);

        allCharacters.Add(ch);
        turnOrder.Add(ch);

        return (row, col);
    }

    /// <summary>
    /// Bir saldırganın belirttiği sütuna göre hedefi grid kurallarıyla (önden-
    /// arkaya, önü boşsa arkaya geçiş) çözüp hasarı uygular. AoE modunda
    /// sütundaki TÜM canlılara, diğer modlarda tek hedefe uygulanır.
    /// </summary>
    public void ResolveColumnAttack(Character attacker, int targetColumn, AbilityTargetingMode mode)
    {
        if (attacker == null) return;

        var enemyGridForAttacker = GetGridFor(!attacker.isPlayer);
        if (enemyGridForAttacker == null) return;

        if (mode == AbilityTargetingMode.AreaOfEffect)
        {
            foreach (var target in enemyGridForAttacker.GetAllAliveInColumn(targetColumn))
                ApplyBasicAttackDamage(attacker, target);
            return;
        }

        var singleTarget = enemyGridForAttacker.ResolveTarget(targetColumn, mode);
        if (singleTarget == null)
        {
            Debug.LogWarning($"[BM] ResolveColumnAttack: sütun {targetColumn} içinde geçerli hedef yok.");
            return;
        }

        ApplyBasicAttackDamage(attacker, singleTarget);
    }

    private void ApplyBasicAttackDamage(Character attacker, Character defender)
    {
        var targetHealth = defender.GetComponent<HealthSystem>();
        if (targetHealth == null) return;

        int attack = Mathf.Max(0, attacker.cardData.str > 0 ? attacker.cardData.str : attacker.cardData.baseStr);
        int defence = Mathf.Max(0, defender.cardData.def > 0 ? defender.cardData.def : defender.cardData.baseDef);
        bool critical = Random.value < BattleRules.BaseCriticalChance;
        int damage = Mathf.Max(1, attack - defence);
        if (critical)
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * BattleRules.CriticalMultiplier));

        targetHealth.ApplyDamageServerSide(damage, critical);

        Debug.Log($"⚔️ {attacker.cardData.cardName} ({attacker.gridRow}/{attacker.gridColumn}) → " +
                  $"{defender.cardData.cardName} ({defender.gridRow}/{defender.gridColumn}) : -{damage} HP");
    }

    /// <summary>
    /// Eski tek Saldır/Yetenek butonunda henüz hedef sütunu UI'ı olmadığı için,
    /// saldırganın karşı sütunu boşsa rakip sahadaki ilk canlı sütuna yönelir.
    /// Böylece kamera yönü/yerleştirme sütunu farkı bir tarafı fiilen ölümsüz
    /// yapmaz. Gerçek sütun seçim UI'ı geri eklendiğinde yalnız seçilen sütun
    /// kullanılmalıdır.
    /// </summary>
    private int ResolveLegacyTargetColumn(Character attacker, AbilityTargetingMode mode)
    {
        if (attacker == null)
            return 0;

        BattleGrid targetGrid = GetGridFor(!attacker.isPlayer);
        int preferredColumn = Mathf.Clamp(attacker.gridColumn, 0, gridColumns - 1);

        if (targetGrid == null ||
            targetGrid.ResolveTarget(preferredColumn, mode) != null)
        {
            return preferredColumn;
        }

        Character fallbackTarget = targetGrid.GetAllAlive()
            .OrderBy(character => character.gridColumn)
            .ThenBy(character => character.gridRow)
            .FirstOrDefault();

        if (fallbackTarget == null)
            return preferredColumn;

        Debug.Log(
            $"[TARGET FALLBACK] {attacker.cardData?.cardName}: " +
            $"{preferredColumn + 1}. sütun boş, {fallbackTarget.gridColumn + 1}. sütun hedeflendi.");

        return fallbackTarget.gridColumn;
    }

    /// <summary>
    /// AKTİF yetenek kullanım isteğini uçtan uca doğrular ve uygular:
    /// yetki, sıra, cooldown hazır mı, raund başına taraf limiti, hedef
    /// çözümü, efekt uygulaması, cooldown'un 4 raunda sıfırlanması ve turun
    /// bitirilmesi.
    /// </summary>
    public bool TryUseActiveAbilityForCharacter(Character character, int targetColumn, ulong senderId, out string reason)
    {
        reason = null;

        if (character == null) { reason = "Karakter yok."; return false; }

        if (!IsSenderAuthorizedForCharacter(senderId, character))
        {
            reason = "Bu karakter üzerinde yetkiniz yok.";
            return false;
        }

        if (!character.isMyTurn)
        {
            reason = "Bu karakterin sırası değil.";
            return false;
        }

        if (!character.IsAlive)
        {
            reason = "Karakter düşmüş durumda.";
            return false;
        }

        if (!character.IsActiveAbilityReady)
        {
            reason = $"Yetenek hazır değil ({character.AbilityCooldownRemaining.Value} raund kaldı).";
            return false;
        }

        if (lastAbilityRoundByClient.TryGetValue(senderId, out int lastUsedRound) &&
            lastUsedRound == CurrentRound)
        {
            reason = "Bu raundda zaten bir aktif yetenek kullandınız.";
            return false;
        }

        if (string.IsNullOrEmpty(character.cardData?.ability))
        {
            reason = "Bu kartın aktif yeteneği tanımlı değil.";
            return false;
        }

        var ability = AbilityCatalog.GetActive(character.cardData.ability);
        if (ability == null)
        {
            reason = $"'{character.cardData.ability}' kimlikli bir yetenek kayıtlı değil.";
            return false;
        }

        var enemyGridForCharacter = GetGridFor(!character.isPlayer);
        Character primaryTarget = enemyGridForCharacter?.ResolveTarget(targetColumn, ability.TargetingMode);

        var allTargets = ability.TargetingMode == AbilityTargetingMode.AreaOfEffect
            ? enemyGridForCharacter?.GetAllAliveInColumn(targetColumn) ?? new List<Character>()
            : (primaryTarget != null ? new List<Character> { primaryTarget } : new List<Character>());

        if (primaryTarget == null && allTargets.Count == 0)
        {
            reason = $"Seçilen {targetColumn + 1}. sütunda geçerli hedef yok.";
            return false;
        }

        var context = new AbilityContext
        {
            Source = character,
            PrimaryTarget = primaryTarget,
            AllTargets = allTargets,
            OwnGrid = GetGridFor(character.isPlayer),
            EnemyGrid = enemyGridForCharacter,
            CurrentRound = CurrentRound
        };

        // Efekt bilgilerini yetenek uygulanmadan önce al.
        // Hedef yetenek sonucunda yok edilse bile konumu elimizde kalır.
        string feedbackAbilityId = character.cardData.ability;
        string sourceVFXKey = GetCharacterVFXKey(character);

        Character visualTarget = primaryTarget != null
            ? primaryTarget
            : allTargets.FirstOrDefault(c => c != null);

        Vector3 skillPosition = visualTarget != null
            ? visualTarget.transform.position
            : character.transform.position;

        // Mevcut oyun mantığı.
        ability.Apply(context);
        character.PlayAnimationClientRpc("Skill");

        character.AbilityCooldownRemaining.Value = 4;
        lastAbilityRoundByClient[senderId] = CurrentRound;

        // Efekti sunucu onayından sonra istemcilere gönder.
        PlayApprovedAbilityFeedbackClientRpc(
            feedbackAbilityId,
            sourceVFXKey,
            skillPosition);

        Debug.Log($"[BM] {character.cardData.cardName} aktif yeteneğini kullandı: {character.cardData.ability} (raund {CurrentRound})");

        EndTurn();
        return true;
    }

    [ClientRpc]
    private void PlayApprovedAbilityFeedbackClientRpc(
        string abilityId,
        string sourceVFXKey,
        Vector3 effectPosition)
    {
        if (VFXManager.Instance != null)
        {
            VFXManager.Instance.PlayCharacterVFX(
                sourceVFXKey,
                "Skill",
                effectPosition);
        }

        // Ses ve görsel efekt birbirini engellemesin.
        if (AudioManager.Instance != null &&
            !string.IsNullOrEmpty(abilityId))
        {
            AudioManager.Instance.PlaySFX(abilityId);
        }
    }

    #endregion
    // ---------------------------------------------------------------------


    void Awake()
    {
        if (Instance == null)
            Instance = this;

        int playersPerSide = Mathf.Clamp(LobbyTeamManager.SelectedPlayersPerTeam, 1, 3);
        BattleTeamSize teamSize = playersPerSide == 3
            ? BattleTeamSize.ThreeVThree
            : playersPerSide == 2
                ? BattleTeamSize.TwoVTwo
                : BattleTeamSize.OneVOne;
        gridColumns = teamSize.GetColumns();
        maxActiveCharactersPerSide = Mathf.Max(maxActiveCharactersPerSide, gridColumns * 3);

        playerGrid = new BattleGrid(gridColumns);
        enemyGrid = new BattleGrid(gridColumns);

        // Gerçek 3D model/prefab yoksa iki cihazda da aynı geçici ağ prefabını hazırla.
        BattleDebugPrefabBootstrap.EnsureRegistered();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleBattleClientDisconnected;

        if (IsClient)
            SubmitLocalSignedDeck();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleBattleClientDisconnected;

        base.OnNetworkDespawn();
    }

    private void HandleBattleClientDisconnected(ulong clientId)
    {
        if (!IsServer || battleEnded)
            return;

        bool disconnectedSide = LobbyTeamManager.IsClientOnPlayerSide(clientId);
        bool wasActive = ActivePlayerClientId.Value == clientId;
        int removedIndex = matchPlayers.IndexOf(clientId);

        matchPlayers.Remove(clientId);
        initialPlacementCompleted.Remove(clientId);
        placedThisTurn.Remove(clientId);
        playerSubmittedCards.Remove(clientId);
        playerSubmittedCardIds.Remove(clientId);
        playerSubmittedJtis.Remove(clientId);
        expectedSubmittedCardCounts.Remove(clientId);
        lastAbilityRoundByClient.Remove(clientId);
        LobbyTeamManager.RemoveClientFromSnapshot(clientId);

        foreach (Character character in allCharacters)
        {
            if (character == null || character.ControllerClientId != clientId)
                continue;

            HealthSystem health = character.GetComponent<HealthSystem>();
            if (health != null)
                health.currentHP.Value = 0;
            character.SetDownedVisual(true);
        }

        bool sideStillConnected = matchPlayers.Any(id =>
            LobbyTeamManager.IsClientOnPlayerSide(id) == disconnectedSide);

        if (!sideStillConnected)
        {
            EndBattle(!disconnectedSide, "team_disconnected");
            return;
        }

        if (wasActive && matchPlayers.Count > 0)
        {
            activePlayerIndex = removedIndex < 0
                ? 0
                : Mathf.Clamp(removedIndex, 0, matchPlayers.Count - 1);
            SetActivePlayer(matchPlayers[activePlayerIndex], drawCard: true);
        }

        CheckVictory();
    }

    private void SubmitLocalSignedDeck()
    {
        List<CardData> deck = null;

        if (StartBattleManager.Instance != null &&
            StartBattleManager.Instance.selectedMatchCards != null &&
            StartBattleManager.Instance.selectedMatchCards.Count > 0)
        {
            deck = StartBattleManager.Instance.selectedMatchCards;
        }
        else if (DeckManagerObject.Instance != null)
        {
            deck = DeckManagerObject.Instance.currentMatchDeck;
        }

        if (deck == null || deck.Count == 0)
        {
            Debug.LogError("[SECURE DECK] Yerel seçili deste bulunamadı.");
            return;
        }

        if (deck.Count < BattleRules.MinimumDeckSize)
        {
            Debug.LogError(
                $"[SECURE DECK] Deste en az {BattleRules.MinimumDeckSize} kart olmalı. " +
                $"Mevcut: {deck.Count}");
            return;
        }

        if (deck.Count > BattleRules.DeckSize)
        {
            Debug.LogError($"[SECURE DECK] Deste {BattleRules.DeckSize} kart sınırını aşıyor.");
            return;
        }

        var tokens = new List<FixedString4096Bytes>();

        foreach (CardData card in deck)
        {
            if (card == null)
            {
                Debug.LogError("[DECK] Deste içinde null kart var.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(card.signedToken))
            {
                tokens.Add(new FixedString4096Bytes(card.signedToken.Trim()));
                continue;
            }

            if (!allowUnsignedTestDeck)
            {
                Debug.LogError(
                    $"[SECURE DECK] '{card.cardName}' kartında imzalı QR kanıtı yok. " +
                    "Kartı yeniden okutun ve desteyi tekrar kaydedin.");
                return;
            }

            // Eski kayıtlarla model/yerleşim testi. TEST kayıtları yayın ekonomisinde
            // güvenilir değildir; Inspector'da allowUnsignedTestDeck kapatılmalıdır.
            string json = JsonUtility.ToJson(CardDataMapper.ToDTO(card));
            string encoded = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            tokens.Add(new FixedString4096Bytes("TEST:" + encoded));
            Debug.LogWarning($"[TEST DECK] '{card.cardName}' imzasız test kartı olarak gönderildi.");
        }

        // NetworkTransport MaxPayloadSize bu projede 6144 bayt. 25 JWT'yi tek
        // RPC dizisinde göndermek paketi sessizce düşürüyordu. Her kart ayrı,
        // güvenilir RPC ile gönderilir; sunucu tüm parçalar gelince desteyi açar.
        for (int i = 0; i < tokens.Count; i++)
            SubmitDeckCardServerRpc(tokens[i], i, tokens.Count);

        Debug.Log($"[DECK CHUNK] {tokens.Count} kart parça parça sunucuya gönderildi.");
    }

    void Start()
    {
        EnsureTurnTimerText();

        actionSelectionUI = GetComponent<BattleActionSelectionUI>();
        if (actionSelectionUI == null)
            actionSelectionUI = gameObject.AddComponent<BattleActionSelectionUI>();
        actionSelectionUI.Initialize(this, attackButton, skillButton);

        // UI buton bağlama (varsa)
        if (startBattleButton != null)
            startBattleButton.onClick.AddListener(OnStartBattleButton);

        if (attackButton == null)
            Debug.LogWarning("[BM] attackButton atanmamış; seçim paneli çalışma anında bir buton oluşturacak.");

        // ✅ Her client kendi elindeki kart destesini görsün
        InitLocalHandUI();

        // Sunucu değilsek server işlemlerine devam etme
        if (!IsServer) return;

        var playerCards = StartBattleManager.Instance?.selectedMatchCards;
        var enemyCards = StartBattleManager.Instance?.enemyMatchCards;

        Debug.Log($"[BM] Start: playerCards={playerCards?.Count}, enemyCards={enemyCards?.Count}");

        if (playerCards == null || enemyCards == null)
        {
            Debug.LogError("❌ BattleManager: Kartlar alınamadı.");
            return;
        }

        Debug.Log($"🟩 Oyuncu kartları sayısı: {playerCards.Count}");
        Debug.Log($"🟥 Düşman kartları sayısı: {enemyCards.Count}");


        // SpawnCharacters(playerCards, enemyCards);

        if (NetworkManager.Singleton.ConnectedClientsList.Count > 0)
            currentTurnClientId = NetworkManager.Singleton.ConnectedClientsList[0].ClientId;
    }

    public void OnFieldAttackButtonPressed()
    {
        if (actionSelectionUI != null)
            actionSelectionUI.TryRequestAttack();
        else
            ShowWarning("Karakter/hedef seçim arayüzü hazır değil.");
    }

    public void OnFieldSkillButtonPressed()
    {
        if (actionSelectionUI != null)
            actionSelectionUI.TryRequestActiveAbility();
        else
            ShowWarning("Karakter/hedef seçim arayüzü hazır değil.");
    }

    // ---------------------------------------------------------------------
    #region UI'dan Savaş Başlatma (opsiyonel)

    public void OnStartBattleButton()
    {
        var playerCards = StartBattleManager.Instance?.selectedMatchCards;
        var enemyCards = StartBattleManager.Instance?.enemyMatchCards;

        if (playerCards == null || enemyCards == null)
        {
            Debug.LogWarning("StartBattle: Kart listeleri bulunamadı.");
            return;
        }

        if (IsServer)
            //SpawnCharacters(playerCards, enemyCards);

            StartBattle();
    }

    #endregion
    // ---------------------------------------------------------------------

    private void InitLocalHandUI()
    {
        Debug.Log($"[BM] InitLocalHandUI çağrıldı. LocalClientId: {NetworkManager.Singleton.LocalClientId}, IsHost: {NetworkManager.Singleton.IsHost}, IsClient: {NetworkManager.Singleton.IsClient}");

        var handUI = Object.FindFirstObjectByType<HandUIManager>(FindObjectsInactive.Include);

        if (handUI == null)
        {
            Debug.LogWarning("[BM] HandUIManager bulunamadı!");
            return;
        }

        List<CardData> deck = null;

        if (StartBattleManager.Instance != null &&
            StartBattleManager.Instance.selectedMatchCards != null &&
            StartBattleManager.Instance.selectedMatchCards.Count > 0)
        {
            deck = StartBattleManager.Instance.selectedMatchCards;
        }
        else if (DeckManagerObject.Instance != null &&
                 DeckManagerObject.Instance.currentMatchDeck != null &&
                 DeckManagerObject.Instance.currentMatchDeck.Count > 0)
        {
            deck = DeckManagerObject.Instance.currentMatchDeck;
        }

        int deckCount = deck != null ? deck.Count : 0;

        Debug.Log($"[BM] Local deck kart sayısı: {deckCount}");

        if (deck == null || deck.Count == 0)
        {
            Debug.LogError("[BM] Local deck boş! HandUI başlatılamadı.");
            return;
        }

        handUI.gameObject.SetActive(true);

        if (handUI.handArea != null)
            handUI.handArea.gameObject.SetActive(true);

        handUI.Init(deck);

        Debug.Log($"[BM] Local HandUI başlatıldı. Kart sayısı: {deck.Count}");
    }


    private void ShowWarning(string message)
    {
        Debug.LogWarning(message);

        if (warningText != null)
            warningText.text = message;
    }

    public void ShowLocalWarning(string message)
    {
        ShowWarning(message);
    }


    #region Oyuncu Kart UI

    // Oyuncu kartlarını UI'da gösterme (CardUI spawn)
    public void SpawnPlayerCards(List<CardData> selectedCards)
    {
        for (int i = 0; i < selectedCards.Count && i < playerGridPositions.Length; i++)
        {
            var cardData = selectedCards[i];
            GameObject go = Instantiate(cardPrefab, playerGridPositions[i].position, Quaternion.identity);
            go.transform.SetParent(playerGridPositions[i], false);

            // Kartı tam olarak pozisyona yerleştir
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var ui = go.GetComponent<CardUI>();
            if (ui != null)
                ui.SetCardData(cardData, false);
        }
    }

    #endregion

    // ---------------------------------------------------------
    // 3D KARAKTER TOPLU SPAWN (oyuncu + düşman)
    // ---------------------------------------------------------
    public void SpawnCharacters(List<CardData> playerCards, List<CardData> enemyCards)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SpawnCharacters sadece sunucu tarafından çağrılabilir.");
            return;
        }

        // ---- Oyuncu ----
        for (int i = 0; i < playerCards.Count && i < playerGridPositions.Length; i++)
        {
            var card = playerCards[i];
            var grid = playerGridPositions[i];

            var prefabToUse = ResolvePrefab(card);
            if (prefabToUse == null) { Debug.LogWarning($"[BM] Prefab yok: {card.cardName}"); continue; }

            if (prefabToUse == characterPrefab)
            {
                Debug.LogWarning($"[Spawn] {card.cardName}: CARD PREFAB YOK → Default kullanılıyor! " +
                                 "QR prefab yolu/isimleri ve Resources konumunu kontrol et.");
            }

            Debug.Log($"[A] Oyuncu spawn -> {card.cardName}, Prefab: {(card.characterPrefab3D ? card.characterPrefab3D.name : "Default")}");

            var obj = Instantiate(prefabToUse, grid.position, grid.rotation);
            var net = obj.GetComponent<NetworkObject>();

            if (net && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
                net.Spawn();

            if (net && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
                net.TrySetParent(grid, false);
            else
                obj.transform.SetParent(grid, false);

            //karakter rotation ve pozisyon x y z
            Vector3 offset = new Vector3(0f, 0f, -282f);
            obj.transform.localPosition = offset;
            obj.transform.localRotation = Quaternion.Euler(-33f, 0f, 0f);
            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.SetPlayerSide(true);
                ch.Setup(card, GetDefaultControllerForSide(true));
                RegisterSpawnedCharacterByFlatIndex(ch, true, i);
            }
        }

        // ---- Düşman ----
        for (int i = 0; i < enemyCards.Count && i < enemyGridPositions.Length; i++)
        {
            var card = enemyCards[i];
            var grid = enemyGridPositions[i];

            var prefabToUse = ResolvePrefab(card);
            if (prefabToUse == null) { Debug.LogWarning($"[BM] Prefab yok (enemy): {card.cardName}"); continue; }

            Debug.Log($"[B] Düşman spawn -> {card.cardName}, Prefab: {(card.characterPrefab3D ? card.characterPrefab3D.name : "Default")}");

            var obj = Instantiate(prefabToUse, grid.position, grid.rotation);
            var net = obj.GetComponent<NetworkObject>();

            if (net && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
                net.Spawn();

            if (net && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
                net.TrySetParent(grid, false);
            else
                obj.transform.SetParent(grid, false);

            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.Euler(20f, 180f, 0f); // rakip bize dönsün
            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.SetPlayerSide(false);
                ch.Setup(card, GetDefaultControllerForSide(false));
                RegisterSpawnedCharacterByFlatIndex(ch, false, i);
            }
        }

        AssignTurnToClient(currentTurnClientId);
    }

    // ---------------------------------------------------------
    // 3D KARAKTER TEK KART SPAWN (elde seçilen kart için)
    // ---------------------------------------------------------
    public void SpawnCharacter(
        CardData card,
        bool isPlayer,
        ulong controllerClientId = ulong.MaxValue)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SpawnCharacter sadece sunucu tarafından çağrılabilir.");
            return;
        }

        Transform[] gridArray = isPlayer ? playerGridPositions : enemyGridPositions;
        int inDef = isPlayer ? nextPlayerSpawnIndex : nextEnemySpawnIndex;

        if (gridArray == null || gridArray.Length == 0)
        {
            Debug.LogWarning("[BM] GridPositions atanmadı.");
            return;
        }

        if (inDef >= gridArray.Length)
        {
            Debug.LogWarning("[BM] Spawn için boş slot kalmadı!");
            return;
        }

        Transform slot = gridArray[inDef];
        if (isPlayer) nextPlayerSpawnIndex++; else nextEnemySpawnIndex++;

        var prefabToUse = ResolvePrefab(card);
        if (prefabToUse == null)
        {
            Debug.LogWarning($"[BM] Prefab yok: {card?.cardName}");
            return;
        }

        var obj = Instantiate(prefabToUse, slot.position, slot.rotation);
        var net = obj.GetComponent<NetworkObject>();

        if (net && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
            net.Spawn();

        // Parent ayarı
        if (net && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
            net.TrySetParent(slot, false);
        else
            obj.transform.SetParent(slot, false);

        // Hizalama
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = isPlayer ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        var ch = obj.GetComponent<Character>();
        if (ch != null)
        {
            ch.SetPlayerSide(isPlayer);
            if (controllerClientId == ulong.MaxValue)
                controllerClientId = GetDefaultControllerForSide(isPlayer);
            ch.Setup(card, controllerClientId);
            RegisterSpawnedCharacterByFlatIndex(ch, isPlayer, inDef);
        }

        Debug.Log($"[BM] Tek kart spawn: {(isPlayer ? "Player" : "Enemy")} -> {card.cardName}");
    }

    // ---------------------------------------------------------------------
    #region Oyuncu Bazlı Tur Sistemi

    // Eski toplu spawn akışı için geriye dönük uyumluluk. Ownership yerine
    // doğrulanmış taraf eşlemesini kullanır.
    public void AssignTurnToClient(ulong clientId)
    {
        if (!IsServer) return;
        foreach (var ch in allCharacters)
            if (ch != null)
                ch.SetTurn(ch.IsAlive &&
                    (ch.ControllerClientId == ulong.MaxValue
                        ? ch.isPlayer == LobbyTeamManager.IsClientOnPlayerSide(clientId)
                        : ch.ControllerClientId == clientId));
    }

    private void BuildMatchPlayerOrder()
    {
        matchPlayers.Clear();

        List<ulong> playerSide = playerSubmittedCards.Keys
            .Where(id => LobbyTeamManager.IsClientOnPlayerSide(id))
            .OrderBy(id => id)
            .ToList();
        List<ulong> enemySide = playerSubmittedCards.Keys
            .Where(id => !LobbyTeamManager.IsClientOnPlayerSide(id))
            .OrderBy(id => id)
            .ToList();

        int longestTeam = Mathf.Max(playerSide.Count, enemySide.Count);
        for (int i = 0; i < longestTeam; i++)
        {
            if (i < playerSide.Count) matchPlayers.Add(playerSide[i]);
            if (i < enemySide.Count) matchPlayers.Add(enemySide[i]);
        }
    }

    private ulong GetDefaultControllerForSide(bool isPlayerSide)
    {
        List<ulong> members = LobbyTeamManager.GetTeamMembers(isPlayerSide ? 0 : 1);
        if (members.Count > 0)
            return members[0];

        if (isPlayerSide)
            return NetworkManager.ServerClientId;

        if (NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                if (client.ClientId != NetworkManager.ServerClientId)
                    return client.ClientId;
        }

        return ulong.MaxValue;
    }

    private void BeginInitialPlacement()
    {
        if (!IsServer) return;

        BuildMatchPlayerOrder();
        int expectedPlayers = Mathf.Clamp(
            LobbyTeamManager.SelectedPlayersPerTeam * 2,
            2,
            6);
        if (matchPlayers.Count < expectedPlayers)
        {
            ShowWarning($"{expectedPlayers} doğrulanmış oyuncu olmadan yerleşim başlayamaz.");
            return;
        }

        battleEnded = false;
        initialPlacementCompleted.Clear();
        placedThisTurn.Clear();
        activePlayerIndex = 0;
        RoundNumber.Value = 0;
        Phase.Value = BattlePhase.InitialPlacement;
        SetActivePlayer(matchPlayers[activePlayerIndex], drawCard: false);
        Debug.Log("[FLOW] İlk yerleşim başladı. Her oyuncu bir kart koyacak.");
    }

    public void StartBattle()
    {
        if (!IsServer) return;
        BeginCombat();
    }

    private void BeginCombat()
    {
        int expectedPlayers = Mathf.Clamp(
            LobbyTeamManager.SelectedPlayersPerTeam * 2,
            2,
            6);
        if (matchPlayers.Count < expectedPlayers)
            BuildMatchPlayerOrder();
        if (matchPlayers.Count < expectedPlayers) return;

        Phase.Value = BattlePhase.PlayerTurn;
        RoundNumber.Value = 1;
        activePlayerIndex = 0;
        placedThisTurn.Clear();
        SetActivePlayer(matchPlayers[activePlayerIndex], drawCard: true);
        Debug.Log("[FLOW] Savaş başladı. Raund 1.");
    }

    private void SetActivePlayer(ulong clientId, bool drawCard)
    {
        ActivePlayerClientId.Value = clientId;
        currentTurnClientId = clientId;

        foreach (var ch in allCharacters)
            if (ch != null)
                ch.SetTurn(ch.IsAlive &&
                    (ch.ControllerClientId == ulong.MaxValue
                        ? ch.isPlayer == LobbyTeamManager.IsClientOnPlayerSide(clientId)
                        : ch.ControllerClientId == clientId));

        ResetTurnTimer();
        BeginLocalTurnClientRpc(clientId, drawCard, Phase.Value, CurrentRound);
    }

    [ClientRpc]
    private void BeginLocalTurnClientRpc(ulong clientId, bool drawCard, BattlePhase phase, int round)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != clientId)
            return;

        if (drawCard)
        {
            var hand = Object.FindFirstObjectByType<HandUIManager>(FindObjectsInactive.Include);
            hand?.DrawCard();
        }

        Debug.Log($"[FLOW] Sıra sende. Faz={phase}, Raund={round}");
    }

    private void OnCardPlaced(ulong senderId)
    {
        if (!IsServer) return;

        if (Phase.Value == BattlePhase.InitialPlacement)
        {
            initialPlacementCompleted.Add(senderId);
            if (initialPlacementCompleted.Count >= matchPlayers.Count)
            {
                BeginCombat();
                return;
            }

            AdvanceActivePlayer(drawCard: false);
            return;
        }

        placedThisTurn.Add(senderId);
    }

    public void EndTurn()
    {
        if (!IsServer || battleEnded || Phase.Value != BattlePhase.PlayerTurn)
            return;

        AdvanceActivePlayer(drawCard: true);
    }

    private void AdvanceActivePlayer(bool drawCard)
    {
        if (matchPlayers.Count == 0) return;

        activePlayerIndex = (activePlayerIndex + 1) % matchPlayers.Count;
        if (activePlayerIndex == 0)
            AdvanceRound();

        SetActivePlayer(matchPlayers[activePlayerIndex], drawCard);
    }

    private void AdvanceRound()
    {
        RoundNumber.Value++;
        placedThisTurn.Clear();
        Debug.Log($"[FLOW] Raund {CurrentRound} başladı.");

        foreach (var ch in allCharacters)
            if (ch != null)
                ch.OnRoundStartServer(CurrentRound);
    }

    #endregion
    // ---------------------------------------------------------------------

    #region AFK / Süre Aşımı

    private void ResetTurnTimer()
    {
        TurnTimeRemaining.Value = turnTimeLimitSeconds;
    }

    void Update()
    {
        UpdateLocalActionButtons();
        UpdateTurnTimerUI();

        if (!IsServer || battleEnded) return;
        if (turnTimeLimitSeconds <= 0f) return; // 0 = zaman sınırı kapalı
        if (Phase.Value != BattlePhase.InitialPlacement && Phase.Value != BattlePhase.PlayerTurn) return;
        if (ActivePlayerClientId.Value == ulong.MaxValue) return;

        if (TurnTimeRemaining.Value > 0f)
        {
            TurnTimeRemaining.Value -= Time.deltaTime;
            return;
        }

        HandleAfkTimeout();
    }

    private void EnsureTurnTimerText()
    {
        if (turnTimerText != null)
            return;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        Canvas targetCanvas = canvases.FirstOrDefault(canvas =>
            canvas != null &&
            canvas.gameObject.scene == gameObject.scene &&
            canvas.renderMode != RenderMode.WorldSpace);

        if (targetCanvas == null)
        {
            Debug.LogWarning("[TURN TIMER] Uygun ekran Canvas'ı bulunamadı; geri sayım UI oluşturulamadı.");
            return;
        }

        GameObject timerObject = new GameObject(
            "TurnTimerText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        timerObject.transform.SetParent(targetCanvas.transform, false);
        timerObject.transform.SetAsLastSibling();

        RectTransform rect = timerObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -24f);
        rect.sizeDelta = new Vector2(360f, 62f);

        TextMeshProUGUI text = timerObject.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 34f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = string.Empty;

        turnTimerText = text;
    }

    private void UpdateTurnTimerUI()
    {
        if (turnTimerText == null)
            return;

        bool timerActive =
            !battleEnded &&
            turnTimeLimitSeconds > 0f &&
            ActivePlayerClientId.Value != ulong.MaxValue &&
            (Phase.Value == BattlePhase.InitialPlacement || Phase.Value == BattlePhase.PlayerTurn);

        if (!timerActive)
        {
            if (turnTimerText.gameObject.activeSelf)
                turnTimerText.gameObject.SetActive(false);
            return;
        }

        if (!turnTimerText.gameObject.activeSelf)
            turnTimerText.gameObject.SetActive(true);

        int seconds = Mathf.Max(0, Mathf.CeilToInt(TurnTimeRemaining.Value));
        bool isLocalTurn =
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClientId == ActivePlayerClientId.Value;

        turnTimerText.text = isLocalTurn
            ? $"HAMLE SÜREN: {seconds} sn"
            : $"RAKİP SÜRESİ: {seconds} sn";
        turnTimerText.color = seconds <= 5
            ? new Color(1f, 0.25f, 0.2f)
            : Color.white;
    }

    private void UpdateLocalActionButtons()
    {
        // Seçim UI saldırgan/hedef/cooldown durumunu da hesaba katar.
        if (actionSelectionUI != null)
            return;

        if (NetworkManager.Singleton == null) return;

        bool myTurn = IsClient &&
                      Phase.Value == BattlePhase.PlayerTurn &&
                      ActivePlayerClientId.Value == NetworkManager.Singleton.LocalClientId;

        if (attackButton != null) attackButton.interactable = myTurn;
        if (skillButton != null) skillButton.interactable = myTurn;
    }

    private void HandleAfkTimeout()
    {
        bool timedOutSideIsPlayer =
            LobbyTeamManager.IsClientOnPlayerSide(ActivePlayerClientId.Value);

        if (timedOutSideIsPlayer)
        {
            playerConsecutiveAfkSkips++;
            enemyConsecutiveAfkSkips = 0;
        }
        else
        {
            enemyConsecutiveAfkSkips++;
            playerConsecutiveAfkSkips = 0;
        }

        Debug.LogWarning($"[BM] AFK/süre aşımı: {(timedOutSideIsPlayer ? "player" : "enemy")} tarafı turunu kaçırdı " +
                          $"({(timedOutSideIsPlayer ? playerConsecutiveAfkSkips : enemyConsecutiveAfkSkips)}. kez üst üste).");

        int strikes = timedOutSideIsPlayer ? playerConsecutiveAfkSkips : enemyConsecutiveAfkSkips;
        if (strikes >= maxConsecutiveAfkSkipsBeforeForfeit)
        {
            EndBattle(winnerIsPlayerSide: !timedOutSideIsPlayer, reason: "afk_timeout");
            return;
        }

        if (Phase.Value == BattlePhase.InitialPlacement)
        {
            EndBattle(winnerIsPlayerSide: !timedOutSideIsPlayer, reason: "initial_placement_timeout");
            return;
        }

        // Normal savaş turunu otomatik atla.
        EndTurn();
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Teslim Olma

    /// <summary>
    /// Teslim olma isteği. 1v1'de tek oy yeterlidir (kendi kendine teslim -
    /// gerçek bir "oylama" iki oyunculu bir takımda anlam kazanır). Takım
    /// modları eklendiğinde requiredVotes = takım boyutu yapılarak buradaki
    /// HashSet tabanlı oy toplama aynen kullanılabilir.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSurrenderServerRpc(ServerRpcParams rpcParams = default)
    {
        if (battleEnded) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!TryResolveSenderSide(senderId, out bool senderIsPlayerSide))
        {
            Debug.LogWarning($"[BM] RequestSurrenderServerRpc: tanınmayan client ({senderId}).");
            return;
        }

        var votes = senderIsPlayerSide ? playerSurrenderVotes : enemySurrenderVotes;
        votes.Add(senderId);

        int teamIndex = senderIsPlayerSide ? 0 : 1;
        int requiredVotes = Mathf.Max(
            1,
            LobbyTeamManager.GetTeamMemberCount(teamIndex));

        if (votes.Count >= requiredVotes)
        {
            Debug.Log($"[BM] {(senderIsPlayerSide ? "Player" : "Enemy")} tarafı teslim oldu.");
            EndBattle(winnerIsPlayerSide: !senderIsPlayerSide, reason: "surrender");
        }
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Maç Sonu

    /// <summary>
    /// Maçı tek, tutarlı bir yerden sonlandırır (AFK forfeit, teslim olma ya
    /// da normal zafer koşulu - CheckVictory de buraya yönlendirilebilir).
    /// Turn timer'ı durdurur, tekrar tetiklenmeyi engeller.
    /// </summary>
    private void EndBattle(bool winnerIsPlayerSide, string reason)
    {
        if (battleEnded) return;
        battleEnded = true;
        Phase.Value = BattlePhase.Finished;
        ActivePlayerClientId.Value = ulong.MaxValue;

        Debug.Log($"[BM] Maç sona erdi. Kazanan taraf: {(winnerIsPlayerSide ? "player" : "enemy")} | sebep: {reason}");

        NotifyMatchEndClientRpc(winnerIsPlayerSide, reason);
    }

    #endregion
    // ---------------------------------------------------------------------





    public void Attack(CardData playedCard, bool isPlayerTurn = true)
    {
        if (playedCard == null)
        {
            Debug.LogWarning("[BM] Attack: Kart null geldi.");
            return;
        }

        Debug.Log($"[BM] Attack çağrıldı → {playedCard.cardName}, isPlayerTurn={isPlayerTurn}");

        SpawnCharacter(playedCard, isPlayerTurn);

        // Eğer oyuncu oynadıysa düşman da spawn etsin
        if (isPlayerTurn)
        {
            var enemyCards = StartBattleManager.Instance?.enemyMatchCards;
            if (enemyCards != null && enemyCards.Count > 0)
            {
                var randomEnemyCard = enemyCards[Random.Range(0, enemyCards.Count)];
                SpawnCharacter(randomEnemyCard, false);
            }
        }

        // ✅ saldırı çözümlemesi
        ResolveAttack(isPlayerTurn);

        EndTurn();
    }



    // ---------------------------------------------------------
    // Düşman (AI) için otomatik kart oynatma
    // ---------------------------------------------------------
    private void EnemyTurn()
    {
        var enemyCards = StartBattleManager.Instance?.enemyMatchCards;
        if (enemyCards == null || enemyCards.Count == 0)
        {
            Debug.LogWarning("[AI] Enemy deck boş.");
            return;
        }

        // Rastgele bir kart seç
        var randomCard = enemyCards[Random.Range(0, enemyCards.Count)];
        Debug.Log($"[AI] Düşman kart oynuyor: {randomCard.cardName}");

        Attack(randomCard, false);
    }

    // ---------------------------------------------------------------------

    #region RPC ve Kart Gönderme

    [ServerRpc(RequireOwnership = false)]
    private void SubmitDeckCardServerRpc(
        FixedString4096Bytes cardProof,
        int cardIndex,
        int totalCards,
        ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (totalCards < BattleRules.MinimumDeckSize || totalCards > BattleRules.DeckSize ||
            cardIndex < 0 || cardIndex >= totalCards)
        {
            Debug.LogWarning($"[DECK CHUNK] Client {senderId} geçersiz parça bilgisi gönderdi: {cardIndex}/{totalCards}");
            return;
        }

        if (cardIndex == 0 || !playerSubmittedCards.ContainsKey(senderId))
        {
            playerSubmittedCards[senderId] = new List<CardData>();
            playerSubmittedCardIds[senderId] = new List<string>();
            playerSubmittedJtis[senderId] = new HashSet<string>();
            expectedSubmittedCardCounts[senderId] = totalCards;
        }

        if (expectedSubmittedCardCounts[senderId] != totalCards)
        {
            Debug.LogWarning($"[DECK CHUNK] Client {senderId} deste boyutunu gönderim ortasında değiştirdi.");
            return;
        }

        string proof = cardProof.ToString();
        if (!TryDecodeSubmittedCard(senderId, proof, out CardData card, out string identity, out string error))
        {
            Debug.LogWarning($"[DECK CHUNK] Client {senderId}, kart {cardIndex} reddedildi: {error}");
            return;
        }

        // İmzasız prototip kartlarında aynı karakter kimliği destede birden
        // fazla bulunabilir. Test modunda sıra indeksini kimliğe ekleyerek
        // 25 parçanın eksiksiz tamamlanmasını sağlarız. İmzalı/JTI kartların
        // tek kullanımlık güvenlik kuralına dokunulmaz.
        if (allowUnsignedTestDeck && identity.StartsWith("TEST:"))
            identity = $"{identity}:SLOT:{cardIndex}";

        HashSet<string> identities = playerSubmittedJtis[senderId];
        if (!identities.Add(identity))
        {
            Debug.LogWarning($"[DECK CHUNK] Client {senderId} aynı fiziksel kartı tekrar gönderdi: {identity}");
            return;
        }

        // Aynı imzalı fiziksel kart iki oyuncunun destesinde bulunamaz.
        // Geliştirme sırasında Host ve Client aynı yerel hesap/desteyle test
        // edilebiliyor. Test modu açıkken iki oyuncudaki aynı imzalı jti'ye
        // geçici izin verilir. Yayında allowUnsignedTestDeck=false olduğunda
        // fiziksel kart çakışması yeniden kesin olarak reddedilir.
        if (!allowUnsignedTestDeck && !identity.StartsWith("TEST:"))
        {
            foreach (var pair in playerSubmittedJtis)
            {
                if (pair.Key != senderId && pair.Value != null && pair.Value.Contains(identity))
                {
                    identities.Remove(identity);
                    Debug.LogWarning("[DECK CHUNK] Aynı fiziksel kart iki oyuncu tarafından gönderildi.");
                    return;
                }
            }
        }

        playerSubmittedCards[senderId].Add(card);
        playerSubmittedCardIds[senderId].Add(card.id);
        Debug.Log($"[DECK CHUNK] Client {senderId}: {playerSubmittedCards[senderId].Count}/{totalCards}");

        if (!AllSubmittedDecksComplete())
            return;

        Debug.Log("[DECK CHUNK] İki deste eksiksiz alındı; ilk yerleşim başlıyor.");
        SpawnCharactersFromSubmittedDecks();
    }

    private bool AllSubmittedDecksComplete()
    {
        int expectedPlayers = Mathf.Clamp(
            LobbyTeamManager.SelectedPlayersPerTeam * 2,
            2,
            6);
        if (playerSubmittedCards.Count < expectedPlayers)
            return false;

        foreach (var pair in playerSubmittedCards)
        {
            if (!expectedSubmittedCardCounts.TryGetValue(pair.Key, out int expected) ||
                pair.Value == null || pair.Value.Count != expected)
                return false;
        }
        return true;
    }

    private bool TryDecodeSubmittedCard(
        ulong senderId, string proof, out CardData card, out string identity, out string error)
    {
        card = null;
        identity = null;
        error = null;

        if (string.IsNullOrWhiteSpace(proof))
        {
            error = "Kart kanıtı boş.";
            return false;
        }

        if (proof.StartsWith("TEST:"))
        {
            if (!allowUnsignedTestDeck)
            {
                error = "İmzasız test modu kapalı.";
                return false;
            }

            try
            {
                string json = Encoding.UTF8.GetString(System.Convert.FromBase64String(proof.Substring(5)));
                CardDTO dto = JsonUtility.FromJson<CardDTO>(json);
                if (dto == null || string.IsNullOrWhiteSpace(dto.id) ||
                    string.IsNullOrWhiteSpace(dto.cardName) || dto.baseHP <= 0 ||
                    dto.baseStr < 0 || dto.baseDef < 0)
                {
                    error = "Test kartı alanları geçersiz.";
                    return false;
                }

                card = CardDataMapper.FromDTO(dto);
                identity = $"TEST:{senderId}:{dto.id}";
                return true;
            }
            catch (System.Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        if (!JwtCardVerifier.TryParse(proof, out CardPayload payload, out error))
            return false;

        // Eski CardMint JWT'leri geçerli ES256 imzasına sahip olduğu hâlde
        // payload içinde typ/ver/jti alanlarını taşımayabiliyor. İmza doğrulandıktan
        // sonra boş typ ve ver=0 geriye dönük format olarak kabul edilir. jti yoksa
        // token'ın SHA-256 özeti benzersiz ve kararlı fiziksel kimlik olur.
        bool typeValid = payload != null &&
            (string.IsNullOrWhiteSpace(payload.typ) || string.Equals(payload.typ, "CARD", System.StringComparison.OrdinalIgnoreCase));
        bool versionValid = payload != null && (payload.ver == 0 || payload.ver == 1);

        if (payload == null || !typeValid || !versionValid ||
            string.IsNullOrWhiteSpace(payload.id) ||
            payload.hp <= 0 || payload.str < 0 || payload.def < 0)
        {
            error = $"İmzalı kart alanları geçersiz: " +
                    $"typ='{payload?.typ}', ver={payload?.ver}, id='{payload?.id}', " +
                    $"hp={payload?.hp}, str={payload?.str}, def={payload?.def}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.name))
            payload.name = payload.id;

        Sprite sprite = DeckManagerObject.Instance != null
            ? DeckManagerObject.Instance.GetSpriteByName(payload.name)
            : null;

        card = new CardData(
            payload.id, payload.name, payload.hp, payload.str, payload.def,
            payload.rarity, payload.ability, payload.passive,
            1, 0, 3, sprite, null, payload.prefab);
        card.signedToken = proof;
        identity = !string.IsNullOrWhiteSpace(payload.jti)
            ? payload.jti
            : "TOKENHASH:" + ComputeSha256Base64(proof);
        return true;
    }

    private static string ComputeSha256Base64(string value)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            return System.Convert.ToBase64String(sha.ComputeHash(bytes));
        }
    }

    // Eski toplu RPC geriye dönük uyumluluk için tutulur; yeni istemci kullanmaz.
    [ServerRpc(RequireOwnership = false)]
    public void SubmitDeckServerRpc(
        FixedString4096Bytes[] signedTokenArray,
        ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (signedTokenArray == null ||
            signedTokenArray.Length < BattleRules.MinimumDeckSize ||
            signedTokenArray.Length > 25)
        {
            Debug.LogWarning(
                $"[SECURE DECK] Client {senderId} geçersiz deste boyutu gönderdi.");
            return;
        }

        var verifiedCards = new List<CardData>();
        var verifiedIds = new List<string>();
        var submittedJtis = new HashSet<string>();

        foreach (FixedString4096Bytes tokenValue in signedTokenArray)
        {
            string token = tokenValue.ToString();

            if (token.StartsWith("TEST:"))
            {
                if (!allowUnsignedTestDeck)
                {
                    Debug.LogWarning($"[SECURE DECK] Client {senderId} imzasız test kartı gönderdi; test modu kapalı.");
                    return;
                }

                try
                {
                    string json = Encoding.UTF8.GetString(System.Convert.FromBase64String(token.Substring(5)));
                    CardDTO dto = JsonUtility.FromJson<CardDTO>(json);
                    if (dto == null || string.IsNullOrWhiteSpace(dto.id) ||
                        string.IsNullOrWhiteSpace(dto.cardName) || dto.baseHP <= 0 ||
                        dto.baseStr < 0 || dto.baseDef < 0)
                    {
                        Debug.LogWarning($"[TEST DECK] Client {senderId} geçersiz test kartı gönderdi.");
                        return;
                    }

                    string testIdentity = $"TEST:{senderId}:{dto.id}";
                    if (!submittedJtis.Add(testIdentity))
                    {
                        Debug.LogWarning($"[TEST DECK] Client {senderId} aynı kartı iki kez gönderdi: {dto.id}");
                        return;
                    }

                    CardData testCard = CardDataMapper.FromDTO(dto);
                    verifiedCards.Add(testCard);
                    verifiedIds.Add(testCard.id);
                    continue;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"[TEST DECK] Kart çözülemedi: {exception.Message}");
                    return;
                }
            }

            if (!JwtCardVerifier.TryParse(token, out CardPayload payload, out string error))
            {
                Debug.LogWarning(
                    $"[SECURE DECK] Client {senderId} için kart reddedildi: {error}");
                return; // Bir kart bile geçersizse tüm deste reddedilir.
            }

            if (payload == null ||
                payload.typ != "CARD" ||
                payload.ver != 1 ||
                string.IsNullOrWhiteSpace(payload.id) ||
                string.IsNullOrWhiteSpace(payload.jti) ||
                payload.hp <= 0 || payload.str < 0 || payload.def < 0)
            {
                Debug.LogWarning(
                    $"[SECURE DECK] Client {senderId} geçersiz kart claim'leri gönderdi.");
                return;
            }

            if (!submittedJtis.Add(payload.jti))
            {
                Debug.LogWarning(
                    $"[SECURE DECK] Client {senderId} aynı fiziksel kartı destede tekrarladı.");
                return;
            }

            foreach (var pair in playerSubmittedJtis)
            {
                if (pair.Key != senderId && pair.Value != null && pair.Value.Contains(payload.jti))
                {
                    Debug.LogWarning(
                        $"[SECURE DECK] Aynı fiziksel kart iki oyuncu tarafından gönderildi (jti çakışması).");
                    return;
                }
            }

            Sprite sprite = DeckManagerObject.Instance != null
                ? DeckManagerObject.Instance.GetSpriteByName(payload.name)
                : null;

            CardData card = new CardData(
                payload.id,
                payload.name,
                payload.hp,
                payload.str,
                payload.def,
                payload.rarity,
                payload.ability,
                payload.passive,
                1,
                0,
                3,
                sprite,
                null,
                payload.prefab
            );

            card.signedToken = token;
            verifiedCards.Add(card);
            verifiedIds.Add(card.id);
        }

        playerSubmittedCards[senderId] = verifiedCards;
        playerSubmittedCardIds[senderId] = verifiedIds;
        playerSubmittedJtis[senderId] = submittedJtis;

        Debug.Log(
            $"[SECURE DECK] Client {senderId}: {verifiedCards.Count} kartın imzası sunucuda doğrulandı.");

        int expectedPlayers = Mathf.Clamp(
            LobbyTeamManager.SelectedPlayersPerTeam * 2,
            2,
            6);
        if (playerSubmittedCards.Count >= expectedPlayers)
        {
            Debug.Log($"[SECURE DECK] {expectedPlayers} doğrulanmış deste hazır.");
            SpawnCharactersFromSubmittedDecks();
        }
    }

    private bool CanStartBattleWithDecks(List<CardData> hostDeck, List<CardData> clientDeck)
    {
        int minCardCount = BattleRules.MinimumDeckSize;

        int hostCount = hostDeck != null ? hostDeck.Count : 0;
        int clientCount = clientDeck != null ? clientDeck.Count : 0;

        if (hostCount < minCardCount || clientCount < minCardCount)
        {
            ShowWarning(
                $"Oyuncuların birinde destesinden {minCardCount} karttan az kart var! " +
                $"Host: {hostCount}, Join: {clientCount}"
            );

            return false;
        }

        ShowWarning("");

        return true;
    }

    private void SpawnCharactersFromSubmittedDecks()
    {
        var players = new List<ulong>(playerSubmittedCards.Keys);
        int expectedPlayers = Mathf.Clamp(
            LobbyTeamManager.SelectedPlayersPerTeam * 2,
            2,
            6);

        if (players.Count < expectedPlayers)
        {
            ShowWarning($"{expectedPlayers} oyuncu doğrulanmış deste göndermeden savaş başlayamaz.");
            return;
        }

        foreach (ulong playerId in players)
        {
            List<CardData> deck = playerSubmittedCards[playerId];
            if (deck == null || deck.Count < BattleRules.MinimumDeckSize)
            {
                ShowWarning(
                    $"Client {playerId} destesinden en az {BattleRules.MinimumDeckSize} kart göndermeli.");
                return;
            }
        }

        int playerSideCount = players.Count(id => LobbyTeamManager.IsClientOnPlayerSide(id));
        int enemySideCount = players.Count - playerSideCount;
        if (playerSideCount != LobbyTeamManager.SelectedPlayersPerTeam ||
            enemySideCount != LobbyTeamManager.SelectedPlayersPerTeam)
        {
            ShowWarning(
                $"Takım dağılımı geçersiz. Ay={playerSideCount}, Kan={enemySideCount}, " +
                $"beklenen={LobbyTeamManager.SelectedPlayersPerTeam}.");
            return;
        }

        ShowWarning("");

        Debug.Log($"{expectedPlayers} oyuncunun desteleri hazır. Savaş başlatılıyor.");

        BeginInitialPlacement();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendSelectedAttackServerRpc(
        ulong attackerNetworkObjectId,
        ulong targetNetworkObjectId,
        ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!IsSendersTurn(senderId, out string turnReason))
        {
            Debug.LogWarning($"[BM] Seçili saldırı reddedildi (client {senderId}): {turnReason}");
            return;
        }

        if (NetworkManager.Singleton == null ||
            NetworkManager.Singleton.SpawnManager == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                attackerNetworkObjectId, out NetworkObject attackerObject) ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                targetNetworkObjectId, out NetworkObject targetObject))
        {
            Debug.LogWarning("[BM] Seçili saldırı reddedildi: karakter NetworkObject bulunamadı.");
            return;
        }

        Character attacker = attackerObject.GetComponent<Character>();
        Character target = targetObject.GetComponent<Character>();

        if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive)
        {
            Debug.LogWarning("[BM] Seçili saldırı reddedildi: saldırgan/hedef geçersiz veya düşmüş.");
            return;
        }

        if (!IsSenderAuthorizedForCharacter(senderId, attacker))
        {
            Debug.LogWarning($"[BM] Seçili saldırı reddedildi: client {senderId} saldırganı kontrol etmiyor.");
            return;
        }

        if (attacker.isPlayer == target.isPlayer)
        {
            Debug.LogWarning("[BM] Seçili saldırı reddedildi: takım arkadaşına saldırılamaz.");
            return;
        }

        BattleGrid targetGrid = GetGridFor(target.isPlayer);
        Character allowedTarget = targetGrid?.ResolveTarget(
            target.gridColumn,
            AbilityTargetingMode.Normal);

        // Seçenek B: seçilen sütunda canlı bir ön sıra varsa orta/arka hedef
        // doğrudan vurulamaz. Kontrol tamamen sunucuda yeniden yapılır.
        if (allowedTarget != target)
        {
            Debug.LogWarning(
                $"[BM] Seçili saldırı reddedildi: {target.cardData?.cardName} ön sıradaki karakter tarafından korunuyor.");
            return;
        }

        attacker.PlayAnimationClientRpc("Attack");
        ApplyBasicAttackDamage(attacker, target);
        EndTurn();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendAttackServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!TryResolveSenderSide(senderId, out bool senderIsPlayerSide))
        {
            Debug.LogWarning($"[BM] Yetkisiz SendAttackServerRpc reddedildi (client {senderId}): taraf çözümlenemedi.");
            return;
        }

        if (Phase.Value != BattlePhase.PlayerTurn)
        {
            Debug.LogWarning($"[BM] Saldırı reddedildi; mevcut faz: {Phase.Value}.");
            return;
        }

        if (!IsSendersTurn(senderId, out string reason))
        {
            Debug.LogWarning($"[BM] Yetkisiz SendAttackServerRpc reddedildi (client {senderId}): {reason}");
            return;
        }

        // UYUMLULUK KATMANI: eski "Saldır" butonu henüz hangi sütuna
        // saldırılacağını seçtiren bir arayüze sahip değil (bkz. yeni
        // Character.AttackServerRpc(int targetColumn) / 6x3 grid sistemi).
        // Makul varsayılan: sıradaki karakter, KENDİ sütununu karşı sahada
        // hedefler ("karşındaki sütuna vur"). Gerçek bir sütun seçim arayüzü
        // eklendiğinde bunun yerine doğrudan Character.AttackServerRpc
        // çağrılmalı ve bu metot kaldırılabilir.
        var attacker = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer == senderIsPlayerSide &&
            (c.ControllerClientId == ulong.MaxValue || c.ControllerClientId == senderId));
        if (attacker == null)
        {
            Debug.LogWarning("[BM] Saldırabilecek canlı karakter yok.");
            return;
        }

        attacker.PlayAnimationClientRpc("Attack");

        int targetColumn = ResolveLegacyTargetColumn(attacker, AbilityTargetingMode.Normal);
        ResolveColumnAttack(attacker, targetColumn, AbilityTargetingMode.Normal);
        EndTurn();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendSkillAttackServerRpc(string ability, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!TryResolveSenderSide(senderId, out bool senderIsPlayerSide))
        {
            Debug.LogWarning($"[BM] Yetkisiz SendSkillAttackServerRpc reddedildi (client {senderId}): taraf çözümlenemedi.");
            return;
        }

        if (!IsSendersTurn(senderId, out string turnErrorReason))
        {
            Debug.LogWarning($"[BM] Yetkisiz SendSkillAttackServerRpc reddedildi (client {senderId}): {turnErrorReason}");
            return;
        }

        // UYUMLULUK KATMANI: bkz. SendAttackServerRpc notu - aynı "kendi
        // sütununu hedefle" varsayımı burada da geçerli. ÖNEMLİ FARK: bu
        // metot artık (eskisi gibi) sadece log basıp turu bitiren boş bir
        // stub DEĞİL - cooldown/3-raund-hazırlık/4-raund-bekleme/raund başına
        // taraf limiti kuralları TryUseActiveAbilityForCharacter içinde TAM
        // olarak uygulanıyor. "ability" parametresi artık kullanılmıyor
        // (yetenek kimliği karakterin kendi CardData.ability alanından
        // okunuyor) - geriye dönük uyumluluk için imza korundu.
        var character = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer == senderIsPlayerSide &&
            (c.ControllerClientId == ulong.MaxValue || c.ControllerClientId == senderId));
        if (character == null)
        {
            Debug.LogWarning("[BM] Yetenek kullanabilecek canlı karakter yok.");
            return;
        }

        IActiveAbility activeAbility = AbilityCatalog.GetActive(character.cardData?.ability);
        AbilityTargetingMode targetingMode = activeAbility != null
            ? activeAbility.TargetingMode
            : AbilityTargetingMode.Normal;
        int targetColumn = ResolveLegacyTargetColumn(character, targetingMode);

        if (!TryUseActiveAbilityForCharacter(character, targetColumn, senderId, out string abilityReason))
        {
            Debug.LogWarning($"[BM] SendSkillAttackServerRpc reddedildi ({character.cardData?.cardName}): {abilityReason}");
        }
        // NOT: EndTurn() burada AYRICA çağrılmıyor - TryUseActiveAbilityForCharacter
        // başarılı olduğunda kendi içinde zaten EndTurn() çağırıyor; başarısız
        // olduğunda ise tur BİLEREK bitirilmiyor (istek reddedildi, oyuncu
        // tekrar deneyebilir/başka bir aksiyon seçebilir).
    }


    [ServerRpc(RequireOwnership = false)]
    public void PlayCardServerRpc(string cardId, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!TryResolveSenderSide(senderClientId, out bool isPlayer))
        {
            Debug.LogWarning($"[BM] PlayCardServerRpc: tanınmayan client reddedildi ({senderClientId}).");
            return;
        }

        var card = FindSubmittedCardForSender(senderClientId, cardId);
        if (card == null)
        {
            Debug.LogWarning("Kart bulunamadı: " + cardId);
            return;
        }

        // Anti-cheat: kart gerçekten bu client'ın maça gönderdiği destede mi?
        if (!SenderOwnsCard(senderClientId, card))
        {
            Debug.LogWarning($"[BM] PlayCardServerRpc reddedildi: client {senderClientId} '{cardId}' kartına sahip değil.");
            return;
        }

        if (!IsSendersTurn(senderClientId, out string turnReason))
        {
            Debug.LogWarning($"[BM] PlayCardServerRpc reddedildi: {turnReason}");
            return;
        }

        if (Phase.Value == BattlePhase.PlayerTurn && placedThisTurn.Contains(senderClientId))
        {
            Debug.LogWarning("[BM] Bu turda zaten bir kart yerleştirildi.");
            return;
        }

        if (!HasFreeSpawnSlot(isPlayer))
        {
            Debug.LogWarning($"[BM] PlayCardServerRpc reddedildi: {(isPlayer ? "player" : "enemy")} tarafında boş slot yok.");
            return;
        }

        SpawnCharacter(card, isPlayer, senderClientId);
        OnCardPlaced(senderClientId);
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Victory

    public void CheckVictory()
    {
        if (!IsServer || battleEnded)
            return;

        int playerAlive = 0;
        int enemyAlive = 0;

        foreach (var ch in allCharacters)
        {
            if (ch == null || !ch.IsAlive)
                continue;

            if (ch.isPlayer)
                playerAlive++;
            else
                enemyAlive++;
        }

        // Henüz iki taraf da sahaya yerleşmemişse maç sonucu üretme.
        if (playerAlive == 0 && enemyAlive == 0)
            return;

        if (playerAlive == 0 && enemyAlive > 0)
        {
            EndBattle(false, "victory");
            return;
        }

        if (enemyAlive == 0 && playerAlive > 0)
        {
            EndBattle(true, "victory");
        }
    }


    /// <summary>
    /// Maç sonucunu HER İKİ client'a da bildirir (ClientRpc, host'un kendi
    /// client instance'ında da çalışır - Netcode'da host = server + client
    /// birleşik). ÖNCEDEN GrantRewardTo SADECE sunucuda (host'un
    /// makinesinde) doğrudan çağrılıyordu; bu yüzden host-olmayan oyuncu
    /// (joiner) ne XP alıyordu ne de maç sonu ekranını görüyordu -
    /// GrantMatchRewards, o an çalıştığı CİHAZDAKİ yerel DeckManagerObject/
    /// CardLevelSystem/SaveSystem verisini günceller, bu yüzden mutlaka her
    /// iki cihazda da AYRI AYRI çalıştırılması gerekiyordu.
    /// </summary>
    [ClientRpc]
    private void NotifyMatchEndClientRpc(bool winnerIsPlayerSide, string reason)
    {
        ulong localId = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClientId
            : NetworkManager.ServerClientId;
        bool localPlayerSide = LobbyTeamManager.IsClientOnPlayerSide(localId);
        bool isLocalPlayerWinner = localPlayerSide == winnerIsPlayerSide;

        Debug.Log($"[BM] Maç sonu bildirimi alındı (client {localId}, taraf: {(localPlayerSide ? "Ay" : "Kan")}) -> " +
                  $"{(isLocalPlayerWinner ? "KAZANDIN" : "KAYBETTİN")} (sebep: {reason})");

        GrantRewardTo(isLocalPlayerWinner);
    }

    public void GrantRewardTo(bool isLocalPlayerWinner)
    {
        var deckManager = Object.FindFirstObjectByType<DeckManagerObject>();
        var usedCards = deckManager != null ? deckManager.currentMatchDeck : null;

        var xpManager = Object.FindFirstObjectByType<MatchEndXPManager>();

        if (xpManager != null)
        {
            xpManager.GrantMatchRewards(isLocalPlayerWinner, usedCards);
        }

        Debug.Log(isLocalPlayerWinner ? "Kazandın! Ödül verildi." : "Kaybettin. Az da olsa XP verildi.");
    }

    #endregion

    private string GetCharacterVFXKey(Character character)
    {
        if (character == null)
            return string.Empty;

        // Karakter oluşturulurken kullanılan prefabı bul.
        // Böylece sahnedeki nesnenin adı değiştirilmiş olsa da
        // mümkün olduğunca gerçek prefab adını kullanırız.
        GameObject prefab = character.cardData != null
            ? ResolvePrefab(character.cardData)
            : null;

        string characterName = prefab != null
            ? prefab.name
            : character.gameObject.name;

        return characterName.Replace("(Clone)", "").Trim();
    }

    private GameObject ResolvePrefab(CardData card)
    {
        // 1) JWT/CSV’den gelen Resources yolu
        if (!string.IsNullOrEmpty(card.prefab))
        {
            var res = Resources.Load<GameObject>(card.prefab);
            if (res != null) return res;
            Debug.LogWarning($"[BM] Resources prefab bulunamadı: {card.prefab}  (Kart: {card.cardName})");
        }

        // 2) Kart üstüne atanmış gerçek prefab
        if (card.characterPrefab3D != null) return card.characterPrefab3D;

        GameObject debugPrefab = BattleDebugPrefabBootstrap.GetPrefab();

        // Modelleme tamamlanana kadar Inspector'da atanmış eski/görünmez default
        // prefabı atlayıp ağ uyumlu kapsülü kullan.
        if (forceDebugCharacterVisuals && debugPrefab != null)
        {
            Debug.Log($"[DEBUG PREFAB] {card?.cardName} görünür test kapsülüyle spawn ediliyor.");
            return debugPrefab;
        }

        // 3) Inspector’dan verilen varsayılan karakter prefabı
        if (characterPrefab != null) return characterPrefab;

        // 4) Model henüz yoksa otomatik üretilen ağ uyumlu test kapsülü
        if (debugPrefab != null)
        {
            Debug.LogWarning($"[BM] {card?.cardName} için 3D model yok; geçici kapsül kullanılıyor.");
            return debugPrefab;
        }

        Debug.LogError("[BM] Prefab seçilemedi; geçici test prefabı da bulunamadı.");
        return null;
    }



    #region UI

    public void ShowEnemyDeck(List<CardData> enemyCards)
    {
        if (enemyCardParent == null || cardSlotPrefab == null) return;

        foreach (Transform child in enemyCardParent)
            Destroy(child.gameObject);

        foreach (CardData card in enemyCards)
        {
            GameObject slot = Instantiate(cardSlotPrefab, enemyCardParent);
            slot.transform.localScale = Vector3.one;

            CardUI ui = slot.GetComponent<CardUI>();
            if (ui != null)
                ui.SetCardData(card, false);
        }
    }



    #endregion



    [ServerRpc(RequireOwnership = false)]
    public void SpawnCharacterAtPositionServerRpc(string cardName, Vector3 position, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        var card = FindSubmittedCardForSender(senderId, cardName);

        if (card == null)
        {
            Debug.LogError("Spawn edilecek kart bulunamadı: " + cardName);

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderId }
                }
            });

            return;
        }

        Debug.Log("TEST CARD => " + card.cardName + " | prefab=" + card.prefab);

        if (!TryResolveSenderSide(senderId, out bool isPlayer))
        {
            Debug.LogWarning($"[BM] SpawnCharacterAtPositionServerRpc: tanınmayan client reddedildi ({senderId}).");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { senderId } }
            });

            return;
        }

        // Anti-cheat: kart gerçekten bu client'ın maça gönderdiği destede mi?
        if (!SenderOwnsCard(senderId, card))
        {
            Debug.LogWarning($"[BM] SpawnCharacterAtPositionServerRpc reddedildi: client {senderId} '{card.cardName}' kartına sahip değil.");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { senderId } }
            });

            return;
        }

        // Anti-cheat: sıra bu client'ın tarafında değilse yerleştirmeye izin verme.
        // (Bu kontrol, "sıram geçmesine rağmen sınırsız saldırı" hatasının kaynağıydı.)
        if (!IsSendersTurn(senderId, out string turnReason))
        {
            Debug.LogWarning($"[BM] SpawnCharacterAtPositionServerRpc reddedildi (client {senderId}): {turnReason}");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { senderId } }
            });

            return;
        }

        if (Phase.Value == BattlePhase.PlayerTurn && placedThisTurn.Contains(senderId))
        {
            Debug.LogWarning("[BM] Bu turda zaten bir kart yerleştirildi.");
            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { senderId } }
            });
            return;
        }

        // Anti-cheat: sahada bu taraf için boş slot yoksa (sınırsız spawn engeli).
        if (!HasFreeSpawnSlot(isPlayer))
        {
            Debug.LogWarning($"[BM] SpawnCharacterAtPositionServerRpc reddedildi: {(isPlayer ? "player" : "enemy")} tarafında boş slot yok.");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { senderId } }
            });

            return;
        }

        Collider zone = isPlayer ? playerZone : enemyZone;

        if (zone == null)
        {
            Debug.LogError("❌ PlayerZone/EnemyZone Collider atanmadı!");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderId }
                }
            });

            return;
        }

        Debug.Log($"Spawn isteyen senderId: {senderId}, isPlayer: {isPlayer}, seçilen zone: {zone.name}, position: {position}");
        Bounds b = zone.bounds;

        bool insideXZ =
            position.x >= b.min.x && position.x <= b.max.x &&
            position.z >= b.min.z && position.z <= b.max.z;

        if (!insideXZ)
        {
            Debug.LogWarning($"❌ {card.cardName} yanlış bölgeye spawn edilmeye çalışıldı. Pos: {position}, Zone: {zone.name}");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderId }
                }
            });

            return;
        }

        var prefabToUse = ResolvePrefab(card);
        if (prefabToUse == null)
        {
            Debug.LogError($"[BM] Prefab yok: {card?.cardName}");

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderId }
                }
            });

            return;
        }



        // Raycast zaten doğru savaş alanı noktasını verir; eski 108/-50/-90
        // ofsetleri karakteri görünür alanın kilometrelerce dışına taşıyordu.
        Vector3 spawnPos = position + Vector3.up * 0.4f;

        Quaternion spawnRot = isPlayer
            ? Quaternion.Euler(0f, 0f, 0f)
            : Quaternion.Euler(0f, 180f, 0f);

        var obj = Instantiate(prefabToUse, spawnPos, spawnRot);

        var net = obj.GetComponent<NetworkObject>();

        if (net != null && IsServer)
            net.Spawn();

        var ch = obj.GetComponent<Character>();

        if (ch != null)
        {
            ch.SetPlayerSide(isPlayer);
            ch.Setup(card, senderId);
            var (placedRow, placedCol) = RegisterSpawnedCharacterByWorldPosition(ch, isPlayer, zone.bounds, position);
            Debug.Log($"[BM] Grid hücresi: {placedRow}/{placedCol}");
        }

        Debug.Log($"✅ {card.cardName} spawn edildi → {(isPlayer ? "Oyuncu" : "Düşman")} bölgesi");
        OnCardPlaced(senderId);

        PlacementResultClientRpc(true, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { senderId }
            }
        });
    }

    private CardData FindCardForSpawn(string key)
    {
        if (DeckManagerObject.Instance == null)
            return null;

        CardData card = DeckManagerObject.Instance.GetCardById(key);
        if (card != null)
            return card;

        string normalizedKey = NormalizeCardKey(key);

        if (!string.IsNullOrEmpty(key) && key.StartsWith("CARD-"))
        {
            string temp = key.Replace("CARD-", "");
            int lastDash = temp.LastIndexOf("-");

            if (lastDash > 0)
                temp = temp.Substring(0, lastDash);

            normalizedKey = NormalizeCardKey(temp);
        }

        return DeckManagerObject.Instance.fullDeck.Find(c =>
            c != null &&
            NormalizeCardKey(c.cardName) == normalizedKey
        );
    }

    private string NormalizeCardKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        value = value.Trim().ToLowerInvariant();
        value = value.Replace("_", " ");

        string normalized = value.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();

        foreach (char ch in normalized)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c");
    }


    private void SpawnRandomEnemyFromDeck()
    {
        var enemyCards = StartBattleManager.Instance?.enemyMatchCards;

        if (enemyCards == null || enemyCards.Count == 0)
        {
            Debug.LogWarning("[BM] Düşman destesi boş, enemy spawn edilemedi.");
            return;
        }

        if (enemyGridPositions == null || enemyGridPositions.Length == 0)
        {
            Debug.LogWarning("[BM] Enemy Grid Positions atanmadı.");
            return;
        }

        if (nextEnemySpawnIndex >= enemyGridPositions.Length)
        {
            Debug.LogWarning("[BM] Düşman spawn slotu kalmadı.");
            return;
        }

        CardData enemyCard = enemyCards[Random.Range(0, enemyCards.Count)];

        Transform slot = enemyGridPositions[nextEnemySpawnIndex];
        int usedSlotIndex = nextEnemySpawnIndex;
        nextEnemySpawnIndex++;

        GameObject prefabToUse = ResolvePrefab(enemyCard);

        if (prefabToUse == null)
        {
            Debug.LogWarning($"[BM] Düşman prefab bulunamadı: {enemyCard.cardName}");
            return;
        }

        Vector3 spawnPos = slot.position + new Vector3(0f, -234.5f, -68f);

        Quaternion spawnRot = Quaternion.Euler(50f, 180f, 0f);

        GameObject obj = Instantiate(prefabToUse, spawnPos, spawnRot);

        var net = obj.GetComponent<NetworkObject>();

        if (net && IsServer)
            net.Spawn();

        Character ch = obj.GetComponent<Character>();

        if (ch != null)
        {
            ch.SetPlayerSide(false);
            ch.Setup(enemyCard);
            RegisterSpawnedCharacterByFlatIndex(ch, false, usedSlotIndex);
        }

        Debug.Log($"🟥 Düşman otomatik spawn edildi: {enemyCard.cardName}");
    }

    [ClientRpc]
    private void PlacementResultClientRpc(bool success, ClientRpcParams clientRpcParams = default)
    {
        if (CharacterPlacer.Instance != null)
            CharacterPlacer.Instance.PlacementResult(success);
    }

    private void ResolveAttack(bool isPlayerTest)
    {
        // Hasar mantığı ve görsel olay bildirimi sunucudan yürüsün.
        if (!IsServer)
            return;

        Character attacker = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer == isPlayerTest);

        Character defender = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer != isPlayerTest);

        if (attacker == null || defender == null)
        {
            Debug.LogWarning("[BM] Saldırı için attacker veya defender yok.");
            return;
        }

        HealthSystem defenderHealth = defender.GetComponent<HealthSystem>();

        if (attacker.cardData == null || defenderHealth == null)
        {
            Debug.LogWarning("[BM] Saldırı için kart veya sağlık verisi eksik.");
            return;
        }

        // Hasar uygulanmadan önce görsel bilgileri al.
        string attackerVFXKey = GetCharacterVFXKey(attacker);
        string defenderVFXKey = GetCharacterVFXKey(defender);

        Vector3 attackPosition = attacker.transform.position;
        Vector3 hitPosition = defender.transform.position;

        int damage = Mathf.Max(1, attacker.cardData.baseStr);

        // Mevcut hasar verme çağrısını koruyoruz.
        defenderHealth.TakeDamageServerRpc(damage);

        // Host dahil istemcilerde görsel geri bildirim oynat.
        PlayAttackFeedbackClientRpc(
            attackerVFXKey,
            defenderVFXKey,
            attackPosition,
            hitPosition);
    }

    [ClientRpc]
    private void PlayAttackFeedbackClientRpc(
        string attackerVFXKey,
        string defenderVFXKey,
        Vector3 attackPosition,
        Vector3 hitPosition)
    {
        if (VFXManager.Instance == null)
            return;

        // Saldıran karakterin saldırı efekti.
        VFXManager.Instance.PlayCharacterVFX(
            attackerVFXKey,
            "Attack",
            attackPosition);

        // Hedef karakterin vuruş alma efekti.
        VFXManager.Instance.PlayCharacterVFX(
            defenderVFXKey,
            "Hit",
            hitPosition);
    }

} // <--- Bu parantez tüm BattleManager sınıfını kapatır.