using TMPro;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using System.Text;


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

    [Tooltip("İstersen sahneden bağla; bağlanmazsa da OnStartBattleButton() çağrılabilir.")]
    public Button startBattleButton;

    [Header("Turn Yönetimi")]
    public ulong currentTurnClientId;

    [Header("Saha Alanları")]
    public Collider playerZone;
    public Collider enemyZone;

    


    // --- RUNTIME ---
    private readonly List<Character> allCharacters = new();
    private readonly Dictionary<ulong, List<string>> playerSubmittedCardIds = new();
    private List<Character> turnOrder = new();
    private int currentIndex = 0;

    // Tek kart spawn için sıralı slot takibi
    private int nextPlayerSpawnIndex = 0;
    private int nextEnemySpawnIndex = 0;

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
    public int CurrentRound { get; private set; } = 1;

    // Raund başına, taraf başına en fazla 1 aktif yetenek kullanımı.
    private int playerSideLastAbilityRound = -1;
    private int enemySideLastAbilityRound = -1;

    [Header("AFK / Süre Aşımı")]
    [Tooltip("Bir karakterin turunu tamamlamak için verilen süre (saniye). 0 = sınırsız (devre dışı).")]
    public float turnTimeLimitSeconds = 30f;

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
        if (senderId == NetworkManager.ServerClientId)
        {
            isPlayerSide = true;
            return true;
        }

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId))
        {
            isPlayerSide = false;
            return true;
        }

        isPlayerSide = false;
        return false; // Bağlı olmayan/tanınmayan bir client - reddet.
    }

    /// <summary>Şu an aktif sıranın, verilen tarafa ait olup olmadığını kontrol eder.</summary>
    private bool IsSendersTurn(bool senderIsPlayerSide, out string reason)
    {
        reason = null;

        if (turnOrder == null || turnOrder.Count == 0 ||
            currentIndex < 0 || currentIndex >= turnOrder.Count ||
            turnOrder[currentIndex] == null)
        {
            reason = "Sıra sistemi henüz hazır değil.";
            return false;
        }

        bool activeSideIsPlayer = turnOrder[currentIndex].isPlayer;
        if (activeSideIsPlayer != senderIsPlayerSide)
        {
            reason = $"Sıra bu tarafta değil (aktif taraf: {(activeSideIsPlayer ? "player" : "enemy")}).";
            return false;
        }

        return true;
    }

    /// <summary>Kartın, gönderen client'ın bu maç için gönderdiği desteye gerçekten ait olup olmadığını kontrol eder.</summary>
    private bool SenderOwnsCard(ulong senderId, CardData card)
    {
        if (card == null) return false;

        if (!playerSubmittedCardIds.TryGetValue(senderId, out var ids) || ids == null)
            return false;

        return (!string.IsNullOrEmpty(card.id) && ids.Contains(card.id)) ||
               (!string.IsNullOrEmpty(card.cardId) && ids.Contains(card.cardId)) ||
               (!string.IsNullOrEmpty(card.cardName) && ids.Contains(card.cardName));
    }

    /// <summary>Verilen tarafın sahada boş spawn slotu (kapasite) olup olmadığını kontrol eder.</summary>
    private bool HasFreeSpawnSlot(bool isPlayerSide)
    {
        int activeCount = allCharacters.Count(c => c != null && c.isPlayer == isPlayerSide);
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
        return character.isPlayer == senderIsPlayerSide;
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

        int damage = Mathf.Max(1, attacker.cardData.baseStr);
        targetHealth.TakeDamageServerRpc(damage);

        Debug.Log($"⚔️ {attacker.cardData.cardName} ({attacker.gridRow}/{attacker.gridColumn}) → " +
                  $"{defender.cardData.cardName} ({defender.gridRow}/{defender.gridColumn}) : -{damage} HP");
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

        int lastUsedRound = character.isPlayer ? playerSideLastAbilityRound : enemySideLastAbilityRound;
        if (lastUsedRound == CurrentRound)
        {
            reason = "Bu raundda bu taraf zaten bir aktif yetenek kullandı.";
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

        var context = new AbilityContext
        {
            Source = character,
            PrimaryTarget = primaryTarget,
            AllTargets = allTargets,
            OwnGrid = GetGridFor(character.isPlayer),
            EnemyGrid = enemyGridForCharacter,
            CurrentRound = CurrentRound
        };

        ability.Apply(context);

        character.AbilityCooldownRemaining.Value = 4; // ActiveAbilityCooldownRounds

        if (character.isPlayer) playerSideLastAbilityRound = CurrentRound;
        else enemySideLastAbilityRound = CurrentRound;

        Debug.Log($"[BM] {character.cardData.cardName} aktif yeteneğini kullandı: {character.cardData.ability} (raund {CurrentRound})");

        EndTurn();
        return true;
    }

    #endregion
    // ---------------------------------------------------------------------


    void Awake()
    {
        if (Instance == null)
            Instance = this;

        playerGrid = new BattleGrid(gridColumns);
        enemyGrid = new BattleGrid(gridColumns);
    }

    void Start()
    {
        // TEST: Eğer ağ henüz başlamadıysa Host olarak başlat
        if (NetworkManager.Singleton &&
            !NetworkManager.Singleton.IsServer &&
            !NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("[BM] Test: Host başladı (Editor/tek cihaz testi).");
        }

        // UI buton bağlama (varsa)
        if (startBattleButton != null)
            startBattleButton.onClick.AddListener(OnStartBattleButton);

        // ✅ Saldır butonu artık sahadaki karakter saldırısı için BattleManager'a bağlı
        if (attackButton != null)
        {
            attackButton.onClick.RemoveAllListeners();
            attackButton.onClick.AddListener(OnFieldAttackButtonPressed);
            Debug.Log("[BM] AttackButton BattleManager'a bağlandı.");
        }
        else
        {
            Debug.LogWarning("[BM] attackButton atanmamış!");
        }

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
        Debug.Log("[BM] Saldır butonuna basıldı. Sahadaki karakter saldırısı çalışıyor.");

        ResolveAttack(true);
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
            obj.transform.localScale = Vector3.one;

            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.Setup(card);
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
            obj.transform.localScale = Vector3.one;

            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.Setup(card);
                RegisterSpawnedCharacterByFlatIndex(ch, false, i);
            }
        }

        AssignTurnToClient(currentTurnClientId);
    }

    // ---------------------------------------------------------
    // 3D KARAKTER TEK KART SPAWN (elde seçilen kart için)
    // ---------------------------------------------------------
    public void SpawnCharacter(CardData card, bool isPlayer)
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
        obj.transform.localScale = Vector3.one;

        var ch = obj.GetComponent<Character>();
        if (ch != null)
        {
            ch.Setup(card);
            RegisterSpawnedCharacterByFlatIndex(ch, isPlayer, inDef);
        }

        Debug.Log($"[BM] Tek kart spawn: {(isPlayer ? "Player" : "Enemy")} -> {card.cardName}");
    }

    // ---------------------------------------------------------------------
    #region Turn Sistemi

    public void AssignTurnToClient(ulong clientId)
    {
        foreach (var ch in allCharacters)
        {
            bool isPlayer = ch.OwnerClientId == clientId;
            ch.SetTurn(isPlayer);
        }
    }

    public void StartBattle()
    {
        // NOT: Önceden turnOrder burada FindObjectsByType ile her seferinde
        // SIFIRDAN kuruluyordu - bu, o ana kadar RegisterSpawnedCharacterBy*
        // ile grid'e kaydedilmiş karakterleri (satır/sütun bilgisiyle birlikte)
        // yok sayıp rastgele sahne sırasıyla değiştiriyordu. Artık turnOrder,
        // karakterler kartla sahaya sürüldükçe zaten dolduruluyor (bkz.
        // RegisterSpawnedCharacterByFlatIndex/ByWorldPosition); burada sadece
        // hâlâ boşsa (ör. eski/alternatif bir çağrı yolu) bir fallback kuruyoruz.
        if (turnOrder == null || turnOrder.Count == 0)
        {
            turnOrder = new List<Character>(
                Object.FindObjectsByType<Character>(FindObjectsSortMode.None));
        }

        currentIndex = 0;
        CurrentRound = 1;
        battleEnded = false;
        AssignTurn();
        ResetTurnTimer();
    }

    public void EndTurn()
    {
        if (battleEnded || turnOrder == null || turnOrder.Count == 0) return;

        if (currentIndex >= 0 && currentIndex < turnOrder.Count && turnOrder[currentIndex] != null)
            turnOrder[currentIndex].SetTurn(false);

        int guard = 0;
        do
        {
            int previousIndex = currentIndex;
            currentIndex = (currentIndex + 1) % turnOrder.Count;

            if (currentIndex <= previousIndex)
                AdvanceRound();

            guard++;
        }
        while (guard < turnOrder.Count &&
               (turnOrder[currentIndex] == null || !turnOrder[currentIndex].IsAlive));

        AssignTurn();
        ResetTurnTimer();
    }

    void AssignTurn()
    {
        if (turnOrder == null || turnOrder.Count == 0) return;
        if (currentIndex < 0 || currentIndex >= turnOrder.Count) return;
        if (turnOrder[currentIndex] == null) return;

        turnOrder[currentIndex].SetTurn(true);
    }

    /// <summary>
    /// turnOrder tam bir tur tamamlayıp başa döndüğünde çağrılır: raund sayacını
    /// artırır, her karakterin pasif yeteneğini (round-start) ve aktif yetenek
    /// cooldown sayacını (sadece canlıyken) günceller.
    /// </summary>
    private void AdvanceRound()
    {
        CurrentRound++;
        Debug.Log($"[BM] === Raund {CurrentRound} başladı ===");

        foreach (var ch in turnOrder)
        {
            if (ch != null)
                ch.OnRoundStartServer(CurrentRound);
        }
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
        if (!IsServer || battleEnded) return;
        if (turnTimeLimitSeconds <= 0f) return; // 0 = zaman sınırı kapalı
        if (turnOrder == null || turnOrder.Count == 0) return;
        if (currentIndex < 0 || currentIndex >= turnOrder.Count || turnOrder[currentIndex] == null) return;

        if (TurnTimeRemaining.Value > 0f)
        {
            TurnTimeRemaining.Value -= Time.deltaTime;
            return;
        }

        HandleAfkTimeout();
    }

    private void HandleAfkTimeout()
    {
        bool timedOutSideIsPlayer = turnOrder[currentIndex].isPlayer;

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

        // Turu otomatik atla (aktif yetenek/saldırı kullanmadan sırayı devret).
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

        // 1v1: taraf başına gereken oy sayısı = 1 (bkz. yukarıdaki not).
        int requiredVotes = 1;

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
    public void SubmitDeckServerRpc(FixedString128Bytes[] cardIdArray, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        List<string> cardIds = cardIdArray.Select(id => id.ToString()).ToList();

        playerSubmittedCardIds[senderId] = cardIds;
        Debug.Log($"📨 {senderId} oyuncusu {cardIds.Count} kart gönderdi.");

        if (playerSubmittedCardIds.Count >= 2)
        {
            Debug.Log("🚀 Her iki oyuncudan deste geldi.");
            SpawnCharactersFromSubmittedDecks();
        }
    }

    private bool CanStartBattleWithDecks(List<CardData> hostDeck, List<CardData> clientDeck)
    {
        int minCardCount = 5;

        int hostCount = hostDeck != null ? hostDeck.Count : 0;
        int clientCount = clientDeck != null ? clientDeck.Count : 0;

        if (hostCount < minCardCount || clientCount < minCardCount)
        {
            ShowWarning(
                $"Oyuncuların birinde destesinden 5 karttan az kart var! " +
                $"Host: {hostCount}, Join: {clientCount}"
            );

            return false;
        }

        ShowWarning("");

        return true;
    }

    private void SpawnCharactersFromSubmittedDecks()
    {
        var players = new List<ulong>(playerSubmittedCardIds.Keys);

        if (players.Count < 2)
        {
            ShowWarning("İki oyuncu deste göndermeden savaş başlayamaz.");
            return;
        }

        var deck1 = playerSubmittedCardIds[players[0]];
        var deck2 = playerSubmittedCardIds[players[1]];

        var deckManager = Object.FindFirstObjectByType<DeckManagerObject>();

        if (deckManager == null)
        {
            ShowWarning("DeckManagerObject bulunamadı.");
            return;
        }

        var hostCards = deck1
            .Select(id => deckManager.GetCardById(id))
            .Where(c => c != null)
            .ToList();

        var joinCards = deck2
            .Select(id => deckManager.GetCardById(id))
            .Where(c => c != null)
            .ToList();

        if (!CanStartBattleWithDecks(hostCards, joinCards))
            return;

        ShowWarning("");

        Debug.Log("İki oyuncunun da en az 5 kartı var. Savaş başlatılıyor.");

        StartBattle();
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

        if (!IsSendersTurn(senderIsPlayerSide, out string reason))
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
        var attacker = turnOrder[currentIndex];
        ResolveColumnAttack(attacker, attacker.gridColumn, AbilityTargetingMode.Normal);

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

        if (!IsSendersTurn(senderIsPlayerSide, out string turnErrorReason))
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
        var character = turnOrder[currentIndex];

        if (!TryUseActiveAbilityForCharacter(character, character.gridColumn, senderId, out string abilityReason))
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

        var deckManager = Object.FindFirstObjectByType<DeckManagerObject>();
        var card = deckManager != null ? deckManager.GetCardById(cardId) : null;
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

        if (!HasFreeSpawnSlot(isPlayer))
        {
            Debug.LogWarning($"[BM] PlayCardServerRpc reddedildi: {(isPlayer ? "player" : "enemy")} tarafında boş slot yok.");
            return;
        }

        SpawnCharacter(card, isPlayer);
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Victory

    public void CheckVictory()
    {
        if (battleEnded) return;

        int alive = 0;
        bool winnerIsPlayerSide = false;

        foreach (var ch in allCharacters)
        {
            if (ch == null) continue;

            // NOT: ch.OwnerClientId burada KASITLI olarak kullanılmıyor. Tüm
            // karakterler net.Spawn() ile (ownership belirtmeden) spawn
            // edildiği için OwnerClientId pratikte her zaman sunucuya eşit
            // olur - host-olmayan taraf için güvenilmez bir kaynaktı.
            // ch.isPlayer, spawn anında TryResolveSenderSide ile doğrulanmış
            // gerçek taraf bilgisini taşır.
            if (ch.IsAlive)
            {
                alive++;
                winnerIsPlayerSide = ch.isPlayer;
            }
        }

        if (alive == 1)
            EndBattle(winnerIsPlayerSide, "victory");
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
        // Host = "player" tarafı (bkz. TryResolveSenderSide). Bu cihazın
        // host mu yoksa katılımcı (joiner) mı olduğuna göre "ben kazandım
        // mı" sorusunu yerel olarak çöz.
        bool isThisDeviceHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        bool isLocalPlayerWinner = isThisDeviceHost ? winnerIsPlayerSide : !winnerIsPlayerSide;

        Debug.Log($"[BM] Maç sonu bildirimi alındı (bu cihaz: {(isThisDeviceHost ? "host" : "joiner")}) -> " +
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

    private GameObject ResolvePrefab(CardData card)
    {
        // 1) JWT/CSV’den gelen Resources yolu
        if (!string.IsNullOrEmpty(card.prefab))
        {
            var res = Resources.Load<GameObject>(card.prefab);
            if (res != null) return res;
            Debug.LogWarning($"[BM] Resources prefab bulunamadı: {card.prefab}  (Kart: {card.cardName})");
        }

        // 2) Kart üstüne atalı prefab (opsiyonel)
        if (card.characterPrefab3D != null) return card.characterPrefab3D;

        // 3) Inspector’dan verdiğin default
        if (characterPrefab != null) return characterPrefab;

        Debug.LogError("[BM] Prefab seçilemedi: characterPrefab da atanmadı!");
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

        var card = FindCardForSpawn(cardName);

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
        if (!IsSendersTurn(isPlayer, out string turnReason))
        {
            Debug.LogWarning($"[BM] SpawnCharacterAtPositionServerRpc reddedildi (client {senderId}): {turnReason}");

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

        Debug.Log($"Spawn isteyen senderId: {senderId}, isPlayer: {isPlayer}, seçilen zone: {zone.name}, position: {position}");

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

   

    Vector3 spawnPos = isPlayer
            ? position + new Vector3(0f, 108f, -50f)
            : position + new Vector3(0f, 108f, -90f);

        Quaternion spawnRot = isPlayer
            ? Quaternion.Euler(0f, 0f, 0f)
            : Quaternion.Euler(0f, 180f, 0f);

        var obj = Instantiate(prefabToUse, spawnPos, spawnRot);

        var net = obj.GetComponent<NetworkObject>();

        if (net != null && IsServer)
            net.Spawn();

        obj.transform.localScale = Vector3.one;

        var ch = obj.GetComponent<Character>();

        if (ch != null)
        {
            ch.Setup(card);
            ch.isPlayer = isPlayer;
            var (placedRow, placedCol) = RegisterSpawnedCharacterByWorldPosition(ch, isPlayer, zone.bounds, position);
            Debug.Log($"[BM] Grid hücresi: {placedRow}/{placedCol}");
        }

        Debug.Log($"✅ {card.cardName} spawn edildi → {(isPlayer ? "Oyuncu" : "Düşman")} bölgesi");

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

        obj.transform.localScale = Vector3.one;

        Character ch = obj.GetComponent<Character>();

        if (ch != null)
        {
            ch.Setup(enemyCard);
            ch.isPlayer = false;
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

    private void ResolveAttack(bool isPlayerTurn)
    {
        Debug.Log("✅ ResolveAttack ÇALIŞTI");
        Debug.Log("🔥 ResolveAttack içine girdim - VFX test başlangıcı");

        if (VFXManager.Instance != null)
        {
            VFXManager.Instance.PlayVFX("Fireball", Vector3.zero);
            Debug.Log("🔥 VFXManager bulundu, Fireball Vector3.zero noktasına çağrıldı");
        }
        else
        {
            Debug.LogError("❌ VFXManager.Instance NULL");
        }


        Debug.Log($"👥 allCharacters COUNT: {allCharacters.Count}");

        foreach (var c in allCharacters)
        {
            if (c == null)
            {
                Debug.Log("❌ NULL character");
                continue;
            }

            var hp = c.GetComponent<HealthSystem>();

            Debug.Log(
                $"CHAR => {c.cardData.cardName} | isPlayer={c.isPlayer} | HP={(hp != null ? hp.currentHP.Value.ToString() : "NO HP")}"
            );
        }


        var attackers = allCharacters
            .Where(c => c != null && c.GetComponent<HealthSystem>().currentHP.Value > 0
                   && (isPlayerTurn ? c.isPlayer : !c.isPlayer))
            .ToList();

        var defenders = allCharacters
            .Where(c => c != null && c.GetComponent<HealthSystem>().currentHP.Value > 0
                   && (isPlayerTurn ? !c.isPlayer : c.isPlayer))
            .ToList();

        if (attackers.Count == 0 || defenders.Count == 0)
        {
            Debug.LogWarning("[BM] Saldırı için attacker veya defender yok.");
            return;
        }

        Character attacker = attackers[0];
        Character defender = defenders[0];

        int damage = Mathf.Max(1, attacker.cardData.baseStr);
        defender.GetComponent<HealthSystem>().TakeDamageServerRpc(damage);

        Debug.Log("🔥 VFX TEST: Fireball çağrıldı");

        if (VFXManager.Instance != null)
        {
            VFXManager.Instance.PlayVFX("Fireball", defender.transform);
        }
        else
        {
            Debug.LogError("❌ VFXManager.Instance NULL");
        }

        Debug.Log($"⚔️ {attacker.cardData.cardName} → {defender.cardData.cardName} | -{damage} HP");

        // NOT: Önceden burada defender MANUEL olarak Destroy() ediliyor ve
        // CheckVictory() tekrar çağrılıyordu. Bu, HealthSystem.Die()'ın
        // (TakeDamageServerRpc HP'yi 0'a indirdiğinde otomatik tetiklediği)
        // "düşür (downed), yok etme" davranışını eziyordu - karakter gerçekten
        // yok ediliyor (revive sistemini kırıyordu), grid'den de hiç
        // kaldırılmıyordu (Remove çağrılmadığı için hücre sonsuza dek "dolu"
        // görünüyordu) ve CheckVictory iki kez çalışıyordu. Ölüm/downed durumu
        // ve zafer kontrolü artık TEK bir yerden (HealthSystem.Die) yönetiliyor.
    }
}