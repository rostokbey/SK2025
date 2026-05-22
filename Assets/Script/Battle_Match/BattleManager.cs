using TMPro;
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
            obj.transform.localRotation = Quaternion.Euler(20f, 180f, 0f); // rakip bize dönsün
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
        turnOrder = new List<Character>(
    Object.FindObjectsByType<Character>(FindObjectsSortMode.None)
);
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

        var deckManager = Object.FindFirstObjectByType<DeckManagerObject>();
        var card = deckManager != null ? deckManager.GetCardById(cardId) : null;
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
    public void SpawnCharacterAtPositionServerRpc(string cardId, Vector3 position, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        var card = DeckManagerObject.Instance.GetCardById(cardId);
        if (card == null)
        {
            Debug.LogError("Spawn edilecek kart bulunamadı: " + cardId);

            PlacementResultClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderId }
                }
            });

            return;
        }

        bool isPlayer = senderId == NetworkManager.ServerClientId;

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
            ? position + new Vector3(0f, 0f, -50f)
            : position + new Vector3(0f, 0f, -90f);

        Quaternion spawnRot = isPlayer
            ? Quaternion.Euler(-30f, 0f, 0f)
            : Quaternion.Euler(50f, 180f, 0f);

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

        // ✅ Oyuncu kart koyduysa karşı tarafa otomatik 1 düşman koy
        if (isPlayer)
        {
            SpawnRandomEnemyFromDeck();
        }

        PlacementResultClientRpc(true, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { senderId }
            }
        });
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
            allCharacters.Add(ch);
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

        if (defender.GetComponent<HealthSystem>().currentHP.Value <= 0)
        {
            Debug.Log($"💀 {defender.cardData.cardName} öldü!");
            allCharacters.Remove(defender);
            Destroy(defender.gameObject);
            CheckVictory();
        }
    }
}