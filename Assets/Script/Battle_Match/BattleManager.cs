using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

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

    void Awake()
    {
        if (Instance == null)
            Instance = this;
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

        // Sunucu değilsek burada erken çık (tek cihaz testinde Host'sun zaten)
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

        // Oyuncu tarafının kart UI'larını göster
        //SpawnPlayerCards(playerCards);

        // Kart listelerini aldıktan sonra: elde 5 kart göster
        var handUI = FindObjectOfType<HandUIManager>(true);
        if (handUI != null)
        {
            var deck = StartBattleManager.Instance != null ? StartBattleManager.Instance.selectedMatchCards : null;
            if (deck != null && deck.Count > 0)
            {
                handUI.Init(deck);
            }
            else
            {
                Debug.LogError("[BM] Deck null veya boş! HandUIManager başlatılamadı.");
            }
        }
        else
        {
            Debug.LogWarning("[BM] HandUIManager bulunamadı!");
        }
        // 🧪 DENEME: Tüm karakterleri anında sahaya basmak istersen açık bırak,
        // kart seçerek spawn akışı deneyeceksen bu satırı YORUM SATIRI yap.
        // SpawnCharacters(playerCards, enemyCards);

        if (NetworkManager.Singleton.ConnectedClientsList.Count > 0)
            currentTurnClientId = NetworkManager.Singleton.ConnectedClientsList[0].ClientId;
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
                allCharacters.Add(ch);
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
            obj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // rakip bize dönsün
            obj.transform.localScale = Vector3.one;

            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.Setup(card);
                allCharacters.Add(ch);
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
        int index = isPlayer ? nextPlayerSpawnIndex : nextEnemySpawnIndex;

        if (gridArray == null || gridArray.Length == 0)
        {
            Debug.LogWarning("[BM] GridPositions atanmadı.");
            return;
        }

        if (index >= gridArray.Length)
        {
            Debug.LogWarning("[BM] Spawn için boş slot kalmadı!");
            return;
        }

        Transform slot = gridArray[index];
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
            allCharacters.Add(ch);
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
        turnOrder = new List<Character>(FindObjectsOfType<Character>());
        currentIndex = 0;
        AssignTurn();
    }

    public void EndTurn()
    {
        if (turnOrder == null || turnOrder.Count == 0) return;

        turnOrder[currentIndex].SetTurn(false);
        currentIndex = (currentIndex + 1) % turnOrder.Count;
        AssignTurn();
    }

    void AssignTurn()
    {
        if (turnOrder == null || turnOrder.Count == 0) return;
        turnOrder[currentIndex].SetTurn(true);
    }

    // ---------------------------------------------------------
    // Oyuncu / Düşman saldırı
    // ---------------------------------------------------------



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

    #endregion
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

    private void SpawnCharactersFromSubmittedDecks()
    {
        var players = new List<ulong>(playerSubmittedCardIds.Keys);
        var deck1 = playerSubmittedCardIds[players[0]];
        var deck2 = playerSubmittedCardIds[players[1]];

        var deckManager = FindObjectOfType<DeckManagerObject>();

        var playerCards = deck1.Select(id => deckManager.GetCardById(id)).Where(c => c != null).ToList();
        var enemyCards = deck2.Select(id => deckManager.GetCardById(id)).Where(c => c != null).ToList();

        //SpawnCharacters(playerCards, enemyCards);
        StartBattle();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendAttackServerRpc() => EndTurn();

    [ServerRpc(RequireOwnership = false)]
    public void SendSkillAttackServerRpc(string ability)
    {
        Debug.Log("Yetenek kullanıldı: " + ability);
        EndTurn();
    }


    [ServerRpc(RequireOwnership = false)]
    public void PlayCardServerRpc(string cardId, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        var card = FindObjectOfType<DeckManagerObject>().GetCardById(cardId);
        if (card == null)
        {
            Debug.LogWarning("Kart bulunamadı: " + cardId);
            return;
        }

        // kim oynadıysa ona göre player/enemy olarak spawn et
        bool isPlayer = senderClientId == NetworkManager.Singleton.ConnectedClientsList[0].ClientId;
        SpawnCharacter(card, isPlayer);
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Victory

    public void CheckVictory()
    {
        int alive = 0;
        ulong winnerId = 0;

        foreach (var ch in allCharacters)
        {
            if (ch == null) continue;

            var hs = ch.GetComponent<HealthSystem>();
            if (hs != null && hs.currentHP.Value > 0)
            {
                alive++;
                winnerId = ch.OwnerClientId;
            }
        }

        if (alive == 1)
            GrantRewardTo(winnerId);
    }


    public void GrantRewardTo(ulong winnerId)
    {
        bool isLocalPlayerWinner = (NetworkManager.Singleton.LocalClientId == winnerId);

        var usedCards = FindObjectOfType<DeckManagerObject>().currentMatchDeck;
        var xpManager = FindObjectOfType<MatchEndXPManager>();

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
    public void SpawnCharacterAtPositionServerRpc(string cardId, Vector3 position, ServerRpcParams rpcParams = default)
    {
        var card = DeckManagerObject.Instance.GetCardById(cardId);
        if (card == null)
        {
            Debug.LogError("Spawn edilecek kart bulunamadı: " + cardId);
            return;
        }

        ulong senderId = rpcParams.Receive.SenderClientId;
        bool isPlayer = senderId == NetworkManager.Singleton.ConnectedClientsList[0].ClientId;

        // --- SAHA KONTROLÜ (BoxCollider) ---
        Collider zone = isPlayer ? playerZone : enemyZone;
        if (zone == null)
        {
            Debug.LogError("❌ PlayerZone/EnemyZone Collider atanmadı!");
            return;
        }

        if (!zone.bounds.Contains(position))
        {
            Debug.LogWarning($"❌ {card.cardName} yanlış bölgeye spawn edilmeye çalışıldı.");
            return;
        }
        // -----------------------------------

        var prefabToUse = ResolvePrefab(card);
        if (prefabToUse == null)
        {
            Debug.LogError($"[BM] Prefab yok: {card?.cardName}");
            return;
        }

        Vector3 spawnPos = position;
        Quaternion spawnRot = isPlayer
            ? Quaternion.Euler(-33f, 0f, 0f)
            : Quaternion.Euler(0f, 180f, 0f);

        var obj = Instantiate(prefabToUse, spawnPos, spawnRot);
        var net = obj.GetComponent<NetworkObject>();

        if (net && IsServer)
            net.Spawn();

        obj.transform.localScale = Vector3.one;

        var ch = obj.GetComponent<Character>();
        if (ch != null)
        {
            ch.Setup(card);
            ch.isPlayer = isPlayer;
            allCharacters.Add(ch);
        }

        Debug.Log($"✅ {card.cardName} spawn edildi → {(isPlayer ? "Oyuncu" : "Düşman")} bölgesi");
    }

    private void ResolveAttack(bool isPlayerTurn)
    {
        var attackers = allCharacters
            .Where(c => c != null && c.GetComponent<HealthSystem>().currentHP.Value > 0
                   && (isPlayerTurn ? c.isPlayer : !c.isPlayer))
            .ToList();

        var defenders = allCharacters
            .Where(c => c != null && c.GetComponent<HealthSystem>().currentHP.Value > 0
                   && (isPlayerTurn ? !c.isPlayer : c.isPlayer))
            .ToList();

        if (attackers.Count == 0 || defenders.Count == 0)
            return;

        Character attacker = attackers[0];
        Character defender = defenders[0];

        int damage = Mathf.Max(1, attacker.cardData.baseDamage);
        defender.GetComponent<HealthSystem>().TakeDamageServerRpc(damage);

        Debug.Log($"⚔️ {attacker.cardData.cardName} → {defender.cardData.cardName} | -{damage} HP");

        if (defender.GetComponent<HealthSystem>().currentHP.Value <= 0)
        {
            Debug.Log($"💀 {defender.cardData.cardName} öldü!");
            allCharacters.Remove(defender);
            Destroy(defender.gameObject);
            CheckVictory();
        }
    }

}