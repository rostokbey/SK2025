using TMPro;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
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

    // Sunucuda oynanmış kartların takibi: client -> gönderilen destedeki kart indeksleri.
    // Hem "aynı kart tekrar oynanamaz" kuralını hem de zafer koşulundaki
    // "elde/destede oynanmamış kart kaldı mı?" sorusunu buradan yanıtlarız.
    private readonly Dictionary<ulong, HashSet<int>> playedCardIndices = new();

    private List<Character> turnOrder = new();
    private int currentIndex = 0;

    // Tek kart spawn için sıralı slot takibi
    private int nextPlayerSpawnIndex = 0;
    private int nextEnemySpawnIndex = 0;

    // Maça özel kimlik: ödülün çift verilmesini önlemek ve backend'e geçişte
    // idempotency anahtarı olarak kullanmak için.
    private string currentMatchId = string.Empty;
    private string lastRewardedMatchId = string.Empty; // yalnızca client tarafında kullanılır

    [Header("Geliştirme / Eski Kayıt Uyumluluğu")]
    [Tooltip("Yalnızca prototip testi için: signedToken taşımayan eski kart kayıtlarını kabul eder. Yayın sürümünde kapatılmalıdır.")]
    public bool allowUnsignedTestDeck = true;

    [Tooltip("Gerçek kart modeli bulunamazsa Inspector'daki eski varsayılan prefab yerine görünür test kapsülünü zorlar.")]
    public bool forceDebugCharacterVisuals = true;

    [Header("Kart Kullanımı")]
    [Tooltip("Açıkken her kart destede yalnızca bir kez oynanabilir (yayın için olması gereken). " +
             "SADECE TEST için kapatılabilir: kapalıyken kart sınırsız oynanabilir ve " +
             "'tüm kartlar oynandı' zafer kuralı çalışmaz.")]
    public bool enforceSingleUseCards = true;

    [Header("Sınırlar (Anti-Cheat)")]
    [Tooltip("Bir tarafın aynı anda sahada bulundurabileceği maksimum karakter sayısı.")]
    public int maxActiveCharactersPerSide = 12;

    [Header("Deste Gönderimi")]
    [Tooltip("Tüm desteler bu süre içinde sunucuya ulaşmazsa maç iptal edilir / eksik taraf kaybeder. 0 = sınırsız.")]
    public float deckSubmitTimeoutSeconds = 45f;
    private float deckWaitStartTime = -1f;

    [Header("Yerleşim / Geri Bildirim")]
    [Tooltip("Serbest tıklamayla yerleştirilen karakteri, çözülen grid hücresinin ortasına hizalar.")]
    public bool snapPlacementToCellCenter = true;

    [Tooltip("Normal saldırıda AudioManager'dan çalınacak SFX anahtarı. Boşsa SFX çalınmaz.")]
    public string basicAttackSfxId = "";

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
    [Tooltip("Bir oyuncunun turunu tamamlaması için verilen süre (saniye). 0 = sınırsız (devre dışı). " +
             "Süre dolarsa oyuncunun yalnızca turu atlanır; mağlup sayılmaz.")]
    public float turnTimeLimitSeconds = BattleRules.SelectionSeconds;

    // Eskiden her karede NetworkVariable<float> güncelleniyordu (sürekli ağ trafiği).
    // Şimdi tur başında yalnızca bitiş zamanı (sunucu saati) bir kez yazılıyor;
    // geri sayımı her client kendi hesaplıyor.
    public readonly NetworkVariable<double> TurnDeadline = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Aktif oyuncunun hamlesi için kalan süre (saniye). Süre sınırsızsa 0.</summary>
    public float TurnTimeRemainingSeconds
    {
        get
        {
            if (TurnDeadline.Value <= 0d || NetworkManager.Singleton == null)
                return 0f;
            return Mathf.Max(0f, (float)(TurnDeadline.Value - NetworkManager.Singleton.ServerTime.Time));
        }
    }

    private bool battleEnded = false;
    private float nextTimerCanvasSearchTime = 0f;
    private bool timerCanvasWarned = false;

    // Teslim olma: 1v1'de tek oyluk "oylama" (kendi kendine teslim), takım
    // modları eklendiğinde requiredVotes takım boyutuna çıkarılabilir.
    private readonly HashSet<ulong> playerSurrenderVotes = new();
    private readonly HashSet<ulong> enemySurrenderVotes = new();

    #endregion
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    #region Yetki / Anti-Cheat Yardımcıları

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

    /// <param name="combatOnly">true ise yalnızca savaş fazında (PlayerTurn) hamleye izin verir;
    /// yerleşim fazında saldırı/yetenek yapılamaz.</param>
    private bool IsSendersTurn(ulong senderId, out string reason, bool combatOnly = false)
    {
        reason = null;

        bool phaseOk = combatOnly
            ? Phase.Value == BattlePhase.PlayerTurn
            : Phase.Value == BattlePhase.InitialPlacement || Phase.Value == BattlePhase.PlayerTurn;

        if (!phaseOk)
        {
            reason = $"Bu fazda hamle yapılamaz: {Phase.Value}.";
            return false;
        }

        if (ActivePlayerClientId.Value != senderId)
        {
            reason = "Sıra sende değil.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Göndericinin destesinde, anahtarla eşleşen ve henüz OYNANMAMIŞ ilk kartı bulur.
    /// Aynı isimli birden fazla kart varsa her çağrıda sıradaki oynanmamış kopya seçilir.
    /// </summary>
    private bool TryFindUnplayedCard(
        ulong senderId, string key, out CardData card, out int cardIndex, out string reason,
        out bool alreadyPlayed)
    {
        card = null;
        cardIndex = -1;
        reason = null;
        alreadyPlayed = false;

        if (string.IsNullOrEmpty(key) ||
            !playerSubmittedCards.TryGetValue(senderId, out var cards) ||
            cards == null)
        {
            reason = "Desten bulunamadı.";
            return false;
        }

        playedCardIndices.TryGetValue(senderId, out HashSet<int> played);
        bool anyMatch = false;
        string matchedName = null;
        string normalizedKey = NormalizeCardKeyForMatch(key);

        // 1. geçiş: birebir eşleşme, 2. geçiş: normalize eşleşme.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                CardData candidate = cards[i];
                if (candidate == null)
                    continue;

                bool match = pass == 0
                    ? string.Equals(candidate.id, key, System.StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(candidate.cardId, key, System.StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(candidate.cardName, key, System.StringComparison.OrdinalIgnoreCase)
                    : NormalizeCardKeyForMatch(candidate.id) == normalizedKey ||
                      NormalizeCardKeyForMatch(candidate.cardId) == normalizedKey ||
                      NormalizeCardKeyForMatch(candidate.cardName) == normalizedKey;

                if (!match)
                    continue;

                anyMatch = true;
                matchedName = string.IsNullOrEmpty(candidate.cardName) ? candidate.id : candidate.cardName;

                if (enforceSingleUseCards && played != null && played.Contains(i))
                    continue;

                card = candidate;
                cardIndex = i;
                return true;
            }
        }

        // Reddedildi: hangi anahtarla, destede ne olduğunu ve hangilerinin "oynandı" sayıldığını yaz.
        string deckDump = string.Join(" | ", cards.Select((c, idx) =>
            $"[{idx}] {(c != null ? c.id + "/" + c.cardName : "null")}" +
            $"{(played != null && played.Contains(idx) ? " *OYNANDI*" : "")}"));
        Debug.LogWarning(
            $"[CARD LOOKUP] Client {senderId}: istenen='{key}' → " +
            $"{(anyMatch ? "eşleşen kart(lar) var ama hepsi oynanmış" : "destede eşleşen kart yok")}. " +
            $"Deste: {deckDump}");

        alreadyPlayed = anyMatch;
        reason = anyMatch
            ? $"'{matchedName}' kartını zaten oynadın."
            : "Bu kart destende bulunamadı.";
        return false;
    }

    private void MarkCardPlayed(ulong senderId, int cardIndex)
    {
        if (!playedCardIndices.TryGetValue(senderId, out HashSet<int> played) || played == null)
        {
            played = new HashSet<int>();
            playedCardIndices[senderId] = played;
        }

        played.Add(cardIndex);
    }

    /// <summary>Bir oyuncunun destesinde henüz oynanmamış (elde + destede) kart sayısı.</summary>
    private int CountRemainingCards(ulong clientId)
    {
        if (!playerSubmittedCards.TryGetValue(clientId, out var cards) || cards == null)
            return 0;

        int played = playedCardIndices.TryGetValue(clientId, out HashSet<int> set) && set != null
            ? set.Count
            : 0;

        return Mathf.Max(0, cards.Count - played);
    }

    /// <summary>Bir tarafın (tüm takım arkadaşları toplamı) oynanmamış kart sayısı.</summary>
    private int CountRemainingCardsForSide(bool isPlayerSide)
    {
        int total = 0;
        foreach (ulong clientId in playerSubmittedCards.Keys)
        {
            if (LobbyTeamManager.IsClientOnPlayerSide(clientId) == isPlayerSide)
                total += CountRemainingCards(clientId);
        }

        return total;
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

    private bool HasFreeSpawnSlot(bool isPlayerSide, out string reason)
    {
        reason = null;
        int activeCount = allCharacters.Count(c => c != null && c.IsAlive && c.isPlayer == isPlayerSide);
        if (activeCount >= maxActiveCharactersPerSide)
        {
            reason = $"Sahada en fazla {maxActiveCharactersPerSide} karakter bulunabilir.";
            return false;
        }

        return true;
    }

    public bool IsSenderAuthorizedForCharacter(ulong senderId, Character character)
    {
        if (character == null) return false;
        if (!TryResolveSenderSide(senderId, out bool senderIsPlayerSide)) return false;

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

    // ---------------------------------------------------------------------
    #region Client Geri Bildirimi (Uyarılar)

    private static ClientRpcParams SendTo(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
    }

    /// <summary>Sunucudan yalnızca ilgili oyuncunun ekranına uyarı gönderir.</summary>
    public void WarnClient(ulong clientId, string message)
    {
        if (!IsServer || string.IsNullOrEmpty(message))
            return;

        ShowWarningClientRpc(message, SendTo(clientId));
    }

    /// <summary>Sunucudan tüm oyuncuların ekranına uyarı gönderir (boş metin uyarıyı temizler).</summary>
    private void BroadcastWarning(string message)
    {
        if (!IsServer)
            return;

        ShowWarningClientRpc(message ?? string.Empty);
    }

    [ClientRpc]
    private void ShowWarningClientRpc(string message, ClientRpcParams clientRpcParams = default)
    {
        ShowLocalWarning(message);
    }

    private void RejectAction(ulong senderId, string context, string reason)
    {
        Debug.LogWarning($"[BM] {context} reddedildi (client {senderId}): {reason}");
        WarnClient(senderId, reason);
    }

    private void RejectPlacement(ulong senderId, string reason)
    {
        Debug.LogWarning($"[BM] Yerleştirme reddedildi (client {senderId}): {reason}");

        ClientRpcParams target = SendTo(senderId);
        PlacementResultClientRpc(false, target);

        if (!string.IsNullOrEmpty(reason))
            ShowWarningClientRpc(reason, target);
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

    /// <summary>Karakterin sırasının olup olmadığını tek yerden hesaplar.</summary>
    private bool ShouldHaveTurn(Character ch, ulong activeClientId)
    {
        if (ch == null || !ch.IsAlive || activeClientId == ulong.MaxValue)
            return false;

        return ch.ControllerClientId == ulong.MaxValue
            ? ch.isPlayer == LobbyTeamManager.IsClientOnPlayerSide(activeClientId)
            : ch.ControllerClientId == activeClientId;
    }

    /// <summary>
    /// Slot tabanlı yerleşimde karakteri gride kaydeder. Hücre alınamazsa false döner
    /// (karakter gridsiz bırakılmaz; çağıran karakteri geri almalıdır).
    /// </summary>
    private bool TryRegisterSpawnedCharacterByFlatIndex(Character ch, bool isPlayerSide, int flatIndex)
    {
        if (ch == null) return false;

        var (row, col) = RowColFromFlatIndex(flatIndex);
        var grid = GetGridFor(isPlayerSide);

        if (grid == null || !grid.TryOccupy(ch, row, col))
            return false;

        allCharacters.Add(ch);
        turnOrder.Add(ch);
        ch.SetTurn(ShouldHaveTurn(ch, ActivePlayerClientId.Value));
        return true;
    }

    /// <summary>
    /// Serbest tıklamayla yerleştirmede karakteri çözülmüş hücreye kaydeder.
    /// Hücre doluysa başka hücreye KAYDIRMAZ (görsel konum ile grid ayrışmasın diye) ve false döner.
    /// </summary>
    private bool TryRegisterSpawnedCharacterByWorldPosition(Character ch, bool isPlayerSide, BattleRow row, int col)
    {
        if (ch == null) return false;

        var grid = GetGridFor(isPlayerSide);
        if (grid == null || !grid.TryOccupy(ch, row, col))
            return false;

        allCharacters.Add(ch);
        turnOrder.Add(ch);
        ch.SetTurn(ShouldHaveTurn(ch, ActivePlayerClientId.Value));
        return true;
    }

    /// <summary>
    /// Hücre kullanılamıyorsa nedenini, uygunsa null döner (Spawn'dan ÖNCE ön kontrol).
    /// BattleGrid ölen karakterleri hücreden boşaltmaz; düşmüş karakter de hücreyi işgal eder,
    /// bu yüzden canlılığa değil doğrudan doluluğa bakılır.
    /// </summary>
    private string GetCellBlockReason(bool isPlayerSide, BattleRow row, int column)
    {
        BattleGrid grid = GetGridFor(isPlayerSide);
        if (grid == null) return "Saha hazır değil.";
        if (!grid.IsValidCell(row, column)) return "Geçersiz hücre.";

        Character occupant = grid.GetOccupant(row, column);
        if (occupant == null) return null;

        return occupant.IsAlive
            ? "Bu hücre dolu. Boş bir hücre seç."
            : "Bu hücrede düşmüş bir karakter var. Başka bir hücre seç.";
    }

    /// <summary>Hiç karakter bulunmayan (düşmüşler dahil) hücre sayısı.</summary>
    private int CountFreeCells(bool isPlayerSide)
    {
        BattleGrid grid = GetGridFor(isPlayerSide);
        if (grid == null) return 0;

        int free = 0;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < grid.Columns; c++)
                if (!grid.IsOccupied((BattleRow)r, c))
                    free++;

        return free;
    }

    /// <summary>
    /// Gerçekten sahaya konabilecek kart sayısı: oynanmamış kartlar ile boş hücre sayısının küçüğü.
    /// Saha düşmüş karakterlerle dolduysa kalan kartlar yerleştirilemez; bu kartlar yüzünden
    /// maç sonsuza kadar bitmemezlik etmesin diye zafer kontrolünde bu değer kullanılır.
    /// </summary>
    private int CountPlayableRemainingCardsForSide(bool isPlayerSide)
    {
        return Mathf.Min(CountRemainingCardsForSide(isPlayerSide), CountFreeCells(isPlayerSide));
    }

    /// <summary>
    /// Bir hücrenin dünya merkezini bulur. BattleGrid'in kendi dünya->hücre çözümleyicisini
    /// bölge üzerinde örnekleyerek bulduğu için hücre sınırlarını ayrıca varsaymaz.
    /// </summary>
    private bool TryGetCellWorldCenter(
        Bounds zone, BattleRow row, int column, bool frontIsMaxZ, float y, out Vector3 center)
    {
        int samplesX = Mathf.Max(16, gridColumns * 8);
        const int samplesZ = 24;

        float sumX = 0f;
        float sumZ = 0f;
        int hits = 0;

        for (int ix = 0; ix < samplesX; ix++)
        {
            float x = Mathf.Lerp(zone.min.x, zone.max.x, (ix + 0.5f) / samplesX);
            for (int iz = 0; iz < samplesZ; iz++)
            {
                float z = Mathf.Lerp(zone.min.z, zone.max.z, (iz + 0.5f) / samplesZ);
                var (r, c) = BattleGrid.ResolveCellFromWorldPosition(
                    zone, new Vector3(x, y, z), gridColumns, frontIsMaxZ);

                if (r == row && c == column)
                {
                    sumX += x;
                    sumZ += z;
                    hits++;
                }
            }
        }

        if (hits == 0)
        {
            center = default;
            return false;
        }

        center = new Vector3(sumX / hits, y, sumZ / hits);
        return true;
    }

    /// <summary>Kayıt başarısız olduysa az önce spawn edilen nesneyi ağdan ve sahneden kaldırır.</summary>
    private static void DiscardSpawned(GameObject obj)
    {
        if (obj == null) return;

        NetworkObject net = obj.GetComponent<NetworkObject>();
        if (net != null && net.IsSpawned)
            net.Despawn(true);
        else
            Destroy(obj);
    }

    private void RefreshTurnFlags(ulong activeClientId)
    {
        foreach (var ch in allCharacters)
            if (ch != null)
                ch.SetTurn(ShouldHaveTurn(ch, activeClientId));
    }

    /// <returns>Saldırı gerçekten uygulandıysa true; geçerli hedef yoksa false.</returns>
    public bool ResolveColumnAttack(Character attacker, int targetColumn, AbilityTargetingMode mode)
    {
        if (attacker == null) return false;
        if (targetColumn < 0 || targetColumn >= gridColumns) return false;

        var enemyGridForAttacker = GetGridFor(!attacker.isPlayer);
        if (enemyGridForAttacker == null) return false;

        if (mode == AbilityTargetingMode.AreaOfEffect)
        {
            // GetAllAliveInColumn her çağrıda yeni liste döndürür; ölümler döngüyü etkilemez.
            List<Character> targets = enemyGridForAttacker.GetAllAliveInColumn(targetColumn);
            if (targets.Count == 0)
                return false;

            BroadcastBasicAttackFeedback(attacker, targets[0]);
            foreach (var target in targets)
                ApplyBasicAttackDamage(attacker, target);
            return true;
        }

        var singleTarget = enemyGridForAttacker.ResolveTarget(targetColumn, mode);
        if (singleTarget == null)
        {
            Debug.LogWarning($"[BM] ResolveColumnAttack: sütun {targetColumn} içinde geçerli hedef yok.");
            return false;
        }

        BroadcastBasicAttackFeedback(attacker, singleTarget);
        ApplyBasicAttackDamage(attacker, singleTarget);
        return true;
    }

    private void ApplyBasicAttackDamage(Character attacker, Character defender)
    {
        if (attacker == null || defender == null) return;

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
    /// Normal saldırının animasyonunu + VFX/SFX'ini tüm client'larda oynatır.
    /// "Hit" efekti hedefte HealthSystem.ShowDamageClientRpc tarafından zaten oynanır.
    /// </summary>
    private void BroadcastBasicAttackFeedback(Character attacker, Character primaryTarget)
    {
        if (attacker == null) return;

        attacker.PlayAnimationClientRpc("Attack");

        string attackerKey = GetCharacterVFXKey(attacker);
        string defenderKey = primaryTarget != null ? GetCharacterVFXKey(primaryTarget) : string.Empty;
        Vector3 attackPosition = GetCharacterVFXPosition(attacker);
        Vector3 hitPosition = primaryTarget != null
            ? GetCharacterVFXPosition(primaryTarget)
            : attackPosition;

        PlayAttackFeedbackClientRpc(attackerKey, defenderKey, attackPosition, hitPosition);
    }

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
    /// Aktif yeteneğin neden kullanılamadığını oyuncuya anlatır. Hem sunucuda hem
    /// client UI'sında kullanılır (yalnızca NetworkVariable tabanlı alanlara bakar).
    /// </summary>
    public static string DescribeAbilityNotReady(Character character)
    {
        if (character == null) return "Karakter yok.";
        if (character.IsStunned) return "Karakter sersemletildi; aksiyon alamaz.";
        if (character.IsBlinded) return "Körlük aktif: aktif yetenek kullanılamaz.";
        if (character.IsSilenced) return "Susturulma aktif: aktif yetenek kullanılamaz.";

        // Character.IsActiveAbilityReady, ilk kullanım için 4 kişisel tik şartı arıyor.
        if (character.PersonalCounter < 4)
            return $"Yetenek henüz hazır değil: {4 - character.PersonalCounter} raund sonra açılır.";

        return $"Yetenek bekleme süresinde: {character.AbilityCooldownRemaining.Value} raund kaldı.";
    }

    private static AbilityTargetSide GetAbilityTargetSide(IActiveAbility ability)
    {
        return ability is IAbilityTargetSide sided ? sided.TargetSide : AbilityTargetSide.Enemy;
    }

    /// <summary>
    /// AKTİF yetenek kullanım isteğini uçtan uca doğrular ve uygular:
    /// yetki, faz, sıra, cooldown hazır mı, raund başına taraf limiti, hedef
    /// çözümü (düşman / takım arkadaşı / kendisi), efekt uygulaması (VFX + SFX),
    /// cooldown'un 4 raunda sıfırlanması ve turun bitirilmesi.
    /// </summary>
    /// <param name="targetRow">Seçilen satır (0-2). Yalnızca AnyTarget modundaki ve takım arkadaşı
    /// hedefli tek hedef yeteneklerde kullanılır; -1 ise sütun kuralı uygulanır.</param>
    public bool TryUseActiveAbilityForCharacter(
        Character character, int targetColumn, ulong senderId, out string reason, int targetRow = -1)
    {
        reason = null;

        if (character == null) { reason = "Karakter yok."; return false; }

        if (battleEnded || Phase.Value != BattlePhase.PlayerTurn)
        {
            reason = "Yetenek yalnızca savaş turunda kullanılabilir.";
            return false;
        }

        if (!IsSenderAuthorizedForCharacter(senderId, character))
        {
            reason = "Bu karakter üzerinde yetkiniz yok.";
            return false;
        }

        if (ActivePlayerClientId.Value != senderId || !character.isMyTurn)
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
            reason = DescribeAbilityNotReady(character);
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

        AbilityTargetSide targetSide = GetAbilityTargetSide(ability);
        BattleGrid ownGrid = GetGridFor(character.isPlayer);
        BattleGrid enemyGridForCharacter = GetGridFor(!character.isPlayer);

        Character primaryTarget = null;
        var allTargets = new List<Character>();

        if (targetSide == AbilityTargetSide.Self)
        {
            primaryTarget = character;
            allTargets.Add(character);
        }
        else
        {
            BattleGrid targetGrid = targetSide == AbilityTargetSide.Ally ? ownGrid : enemyGridForCharacter;
            if (targetGrid == null)
            {
                reason = "Saha hazır değil.";
                return false;
            }

            if (targetColumn < 0 || targetColumn >= gridColumns)
            {
                reason = "Geçersiz sütun seçildi.";
                return false;
            }

            bool isAoe = ability.TargetingMode == AbilityTargetingMode.AreaOfEffect;
            bool rowGiven = targetRow >= 0 && targetRow <= 2;

            if (!isAoe && rowGiven &&
                (targetSide == AbilityTargetSide.Ally || ability.TargetingMode == AbilityTargetingMode.AnyTarget))
            {
                // "Herhangi bir hedefi seç" ve takım arkadaşı hedefli yeteneklerde oyuncunun
                // seçtiği hücre esas alınır (BattleGrid.ResolveTarget yalnızca sütuna bakar).
                Character picked = targetGrid.GetOccupant((BattleRow)targetRow, targetColumn);
                if (picked != null && picked.IsAlive)
                    primaryTarget = picked;
                else if (targetSide != AbilityTargetSide.Ally)
                    primaryTarget = targetGrid.ResolveTarget(targetColumn, ability.TargetingMode);
            }
            else
            {
                primaryTarget = targetGrid.ResolveTarget(targetColumn, ability.TargetingMode);
            }

            if (isAoe)
                allTargets = targetGrid.GetAllAliveInColumn(targetColumn); // her çağrıda yeni liste döner
            else if (primaryTarget != null)
                allTargets.Add(primaryTarget);
        }

        if (primaryTarget == null && allTargets.Count == 0)
        {
            reason = targetSide == AbilityTargetSide.Ally
                ? $"Seçilen {targetColumn + 1}. sütunda takım arkadaşı yok."
                : $"Seçilen {targetColumn + 1}. sütunda geçerli hedef yok.";
            return false;
        }

        var context = new AbilityContext
        {
            Source = character,
            PrimaryTarget = primaryTarget,
            AllTargets = allTargets,
            OwnGrid = ownGrid,
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

        Vector3 sourcePosition = GetCharacterVFXPosition(character);
        Vector3 targetPosition = visualTarget != null
            ? GetCharacterVFXPosition(visualTarget)
            : sourcePosition;

        ability.Apply(context);
        character.PlayAnimationClientRpc("Skill");

        character.AbilityCooldownRemaining.Value = 4;
        lastAbilityRoundByClient[senderId] = CurrentRound;

        PlayApprovedAbilityFeedbackClientRpc(
            feedbackAbilityId,
            sourceVFXKey,
            sourcePosition,
            targetPosition,
            targetSide != AbilityTargetSide.Self);

        if (primaryTarget != null)
            Debug.Log($"💡 {character.cardData.cardName} beceri kullandı: {character.cardData.ability} → {primaryTarget.cardData.cardName} (raund {CurrentRound})");
        else
            Debug.Log($"[BM] {character.cardData.cardName} aktif yeteneğini kullandı: {character.cardData.ability} (raund {CurrentRound})");

        EndTurn();
        return true;
    }

    [ClientRpc]
    private void PlayApprovedAbilityFeedbackClientRpc(
        string abilityId,
        string sourceVFXKey,
        Vector3 sourcePosition,
        Vector3 targetPosition,
        bool playProjectile)
    {
        if (VFXManager.Instance != null)
        {
            // 1) Büyü başı — karakter belinde
            VFXManager.Instance.PlayVFX(sourceVFXKey + "_Cast", sourcePosition);

            // 2) Karakterden hedefe giden mermi/büyü (kendine uygulanan yetenekte mermi yok)
            if (playProjectile)
            {
                VFXManager.Instance.PlayProjectile(
                    sourceVFXKey + "_Projectile",
                    sourcePosition,
                    targetPosition,
                    35f);
            }

            // 3) Hedefte patlama
            VFXManager.Instance.PlayVFX(sourceVFXKey + "_Impact", targetPosition);
        }

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(abilityId))
            AudioManager.Instance.PlaySFX(abilityId);
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

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleBattleClientDisconnected;
            NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
        }

        if (IsServer)
            deckWaitStartTime = Time.unscaledTime;

        if (IsClient)
            SubmitLocalSignedDeck();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleBattleClientDisconnected;
            NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
        }

        base.OnNetworkDespawn();
    }

    private void HandleClientStopped(bool wasHost)
    {
        // Sadece join tarafı için. Host tarafında zaten EndBattle var.
        if (wasHost) return;
        if (IsServer) return;

        Debug.Log("[BM] Server durdu; host oyundan ayrıldı, sonuç ekranı gösteriliyor.");

        MatchEndUI.Show(
            isWinner: true,
            reason: "host_disconnected",
            rewardText: "Rakip oyundan ayrıldı. Tecrübe kazandın.");
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
        playedCardIndices.Remove(clientId);
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

        Debug.Log($"[BM] Disconnect: client {clientId}, taraf bagli mi: {sideStillConnected}, matchPlayers: {matchPlayers.Count}");

        if (!sideStillConnected)
        {
            EndBattle(!disconnectedSide, "team_disconnected");
            return;
        }

        if (wasActive && matchPlayers.Count > 0)
        {
            if (removedIndex >= matchPlayers.Count)
            {
                // Ayrılan son sıradaydı: başa dön ve yeni raund başlat.
                activePlayerIndex = 0;
                if (Phase.Value == BattlePhase.PlayerTurn)
                    AdvanceRound();
            }
            else
            {
                // Sonraki oyuncu aynı indekse kaydı.
                activePlayerIndex = Mathf.Max(0, removedIndex);
            }

            SetActivePlayer(matchPlayers[activePlayerIndex], drawCard: Phase.Value == BattlePhase.PlayerTurn);
        }
        else if (removedIndex >= 0 && removedIndex < activePlayerIndex)
        {
            // Sırası gelmemiş biri ayrıldı: aktif oyuncunun indeksi bir kaydı.
            activePlayerIndex--;
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
            ReportLocalDeckFailure("Yerel seçili deste bulunamadı.");
            return;
        }

        if (deck.Count < BattleRules.MinimumDeckSize)
        {
            ReportLocalDeckFailure(
                $"Deste en az {BattleRules.MinimumDeckSize} kart olmalı. Mevcut: {deck.Count}");
            return;
        }

        if (deck.Count > BattleRules.DeckSize)
        {
            ReportLocalDeckFailure($"Deste {BattleRules.DeckSize} kart sınırını aşıyor.");
            return;
        }

        var tokens = new List<FixedString4096Bytes>();

        foreach (CardData card in deck)
        {
            if (card == null)
            {
                ReportLocalDeckFailure("Deste içinde boş (null) kart var.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(card.signedToken))
            {
                tokens.Add(new FixedString4096Bytes(card.signedToken.Trim()));
                continue;
            }

            if (!allowUnsignedTestDeck)
            {
                ReportLocalDeckFailure(
                    $"'{card.cardName}' kartında imzalı QR kanıtı yok. " +
                    "Kartı yeniden okutun ve desteyi tekrar kaydedin.");
                return;
            }

            string json = JsonUtility.ToJson(CardDataMapper.ToDTO(card));
            string encoded = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            tokens.Add(new FixedString4096Bytes("TEST:" + encoded));
            Debug.LogWarning($"[TEST DECK] '{card.cardName}' imzasız test kartı olarak gönderildi.");
        }

        for (int i = 0; i < tokens.Count; i++)
            SubmitDeckCardServerRpc(tokens[i], i, tokens.Count);

        Debug.Log($"[DECK CHUNK] {tokens.Count} kart parça parça sunucuya gönderildi.");
    }

    /// <summary>
    /// Deste hiç gönderilemediyse sessizce beklemek yerine hem oyuncuyu uyarır
    /// hem de sunucuya bildirir (aksi halde iki taraf da sonsuza kadar bekler).
    /// </summary>
    private void ReportLocalDeckFailure(string message)
    {
        Debug.LogError($"[SECURE DECK] {message}");
        ShowLocalWarning($"Deste gönderilemedi: {message}");

        string trimmed = message != null && message.Length > 200 ? message.Substring(0, 200) : message;
        ReportDeckFailureServerRpc(trimmed ?? string.Empty);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportDeckFailureServerRpc(string message, ServerRpcParams rpcParams = default)
    {
        FailDeckSubmission(rpcParams.Receive.SenderClientId, "istemci bildirdi: " + message);
    }

    /// <summary>Destesi geçersiz/eksik olan oyuncunun tarafını mağlup sayar (oynanış olmadığı için ödül yok).</summary>
    private void FailDeckSubmission(ulong clientId, string reason)
    {
        if (!IsServer || battleEnded || Phase.Value != BattlePhase.WaitingForDecks)
            return;

        if (!TryResolveSenderSide(clientId, out bool side))
            return;

        Debug.LogWarning($"[DECK] Client {clientId} destesi reddedildi: {reason}");
        WarnClient(clientId, $"Deste doğrulanamadı: {reason}");
        EndBattle(winnerIsPlayerSide: !side, reason: "deck_invalid");
    }

    private bool IsSideDeckComplete(bool isPlayerSide)
    {
        int needed = Mathf.Max(1, LobbyTeamManager.SelectedPlayersPerTeam);
        int complete = 0;

        foreach (var pair in playerSubmittedCards)
        {
            if (LobbyTeamManager.IsClientOnPlayerSide(pair.Key) != isPlayerSide)
                continue;

            if (expectedSubmittedCardCounts.TryGetValue(pair.Key, out int expected) &&
                pair.Value != null && pair.Value.Count == expected)
                complete++;
        }

        return complete >= needed;
    }

    private void HandleDeckSubmitTimeout()
    {
        bool playerReady = IsSideDeckComplete(true);
        bool enemyReady = IsSideDeckComplete(false);

        Debug.LogWarning($"[DECK] Deste gönderim süresi doldu. Player hazır={playerReady}, Enemy hazır={enemyReady}.");

        if (playerReady && !enemyReady)
            EndBattle(true, "deck_timeout");
        else if (!playerReady && enemyReady)
            EndBattle(false, "deck_timeout");
        else
            AbortBattle("deck_timeout");
    }

    void Start()
    {
        EnsureTurnTimerText();

        actionSelectionUI = GetComponent<BattleActionSelectionUI>();
        if (actionSelectionUI == null)
            actionSelectionUI = gameObject.AddComponent<BattleActionSelectionUI>();
        actionSelectionUI.Initialize(this, attackButton, skillButton);

        if (startBattleButton != null)
            startBattleButton.onClick.AddListener(OnStartBattleButton);

        if (attackButton == null)
            Debug.LogWarning("[BM] attackButton atanmamış; seçim paneli çalışma anında bir buton oluşturacak.");

        InitLocalHandUI();

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

    public void SpawnPlayerCards(List<CardData> selectedCards)
    {
        for (int i = 0; i < selectedCards.Count && i < playerGridPositions.Length; i++)
        {
            var cardData = selectedCards[i];
            GameObject go = Instantiate(cardPrefab, playerGridPositions[i].position, Quaternion.identity);
            go.transform.SetParent(playerGridPositions[i], false);

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

            Vector3 offset = new Vector3(0f, 0f, -282f);
            obj.transform.localPosition = offset;
            obj.transform.localRotation = Quaternion.Euler(-33f, 0f, 0f);
            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.SetPlayerSide(true);
                ch.Setup(card, GetDefaultControllerForSide(true));
                if (!TryRegisterSpawnedCharacterByFlatIndex(ch, true, i))
                    DiscardSpawned(obj);
            }
        }

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
            obj.transform.localRotation = Quaternion.Euler(20f, 180f, 0f);
            var ch = obj.GetComponent<Character>();
            if (ch != null)
            {
                ch.SetPlayerSide(false);
                ch.Setup(card, GetDefaultControllerForSide(false));
                if (!TryRegisterSpawnedCharacterByFlatIndex(ch, false, i))
                    DiscardSpawned(obj);
            }
        }

        AssignTurnToClient(currentTurnClientId);
    }

    // ---------------------------------------------------------
    // 3D KARAKTER TEK KART SPAWN (elde seçilen kart için)
    // ---------------------------------------------------------
    /// <returns>Spawn edilen karakter; slot/prefab/grid sorunu varsa null.
    /// Slot sayacı yalnızca başarılı spawn'da ilerler.</returns>
    public Character SpawnCharacter(
        CardData card,
        bool isPlayer,
        ulong controllerClientId = ulong.MaxValue)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SpawnCharacter sadece sunucu tarafından çağrılabilir.");
            return null;
        }

        if (card == null)
        {
            Debug.LogWarning("[BM] SpawnCharacter: kart null.");
            return null;
        }

        Transform[] gridArray = isPlayer ? playerGridPositions : enemyGridPositions;
        int slotIndex = isPlayer ? nextPlayerSpawnIndex : nextEnemySpawnIndex;

        if (gridArray == null || gridArray.Length == 0)
        {
            Debug.LogWarning("[BM] GridPositions atanmadı.");
            return null;
        }

        if (slotIndex >= gridArray.Length)
        {
            Debug.LogWarning("[BM] Spawn için boş slot kalmadı!");
            return null;
        }

        var prefabToUse = ResolvePrefab(card);
        if (prefabToUse == null)
        {
            Debug.LogWarning($"[BM] Prefab yok: {card.cardName}");
            return null;
        }

        Transform slot = gridArray[slotIndex];

        var obj = Instantiate(prefabToUse, slot.position, slot.rotation);
        var net = obj.GetComponent<NetworkObject>();
        var ch = obj.GetComponent<Character>();

        if (ch == null || net == null)
        {
            Debug.LogWarning($"[BM] {card.cardName}: prefab'ta Character/NetworkObject yok.");
            Destroy(obj);
            return null;
        }

        net.Spawn();
        net.TrySetParent(slot, false);

        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = isPlayer ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

        ch.SetPlayerSide(isPlayer);
        if (controllerClientId == ulong.MaxValue)
            controllerClientId = GetDefaultControllerForSide(isPlayer);
        ch.Setup(card, controllerClientId);

        if (!TryRegisterSpawnedCharacterByFlatIndex(ch, isPlayer, slotIndex))
        {
            Debug.LogWarning($"[BM] {card.cardName}: grid hücresi alınamadı, karakter geri alındı.");
            DiscardSpawned(obj);
            return null;
        }

        if (isPlayer) nextPlayerSpawnIndex++; else nextEnemySpawnIndex++;

        Debug.Log($"[BM] Tek kart spawn: {(isPlayer ? "Player" : "Enemy")} -> {card.cardName}");
        return ch;
    }

    // ---------------------------------------------------------------------
    #region Oyuncu Bazlı Tur Sistemi

    public void AssignTurnToClient(ulong clientId)
    {
        if (!IsServer) return;
        RefreshTurnFlags(clientId);
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
            BroadcastWarning($"{expectedPlayers} doğrulanmış oyuncu olmadan yerleşim başlayamaz.");
            return;
        }

        battleEnded = false;
        currentMatchId = System.Guid.NewGuid().ToString("N");
        playedCardIndices.Clear();
        initialPlacementCompleted.Clear();
        placedThisTurn.Clear();
        activePlayerIndex = 0;
        RoundNumber.Value = 0;
        Phase.Value = BattlePhase.InitialPlacement;
        SetActivePlayer(matchPlayers[activePlayerIndex], drawCard: false);
        Debug.Log($"[FLOW] İlk yerleşim başladı (maç {currentMatchId}). Her oyuncu bir kart koyacak.");
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

        RefreshTurnFlags(clientId);

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
            CompleteInitialPlacementFor(senderId);
            return;
        }

        placedThisTurn.Add(senderId);

        // Karşı tarafta canlı karakter yoksa saldıracak kimse de yok:
        // kartı koyduktan sonra süre dolmasını beklemeden turu bitir.
        if (!AnyAliveOpponentFor(senderId))
            EndTurn();
    }

    /// <summary>Yerleşim fazında bir oyuncunun sırası tamamlandı (kart koydu ya da süresi doldu).</summary>
    private void CompleteInitialPlacementFor(ulong clientId)
    {
        initialPlacementCompleted.Add(clientId);

        if (initialPlacementCompleted.Count >= matchPlayers.Count)
        {
            BeginCombat();
            return;
        }

        AdvanceActivePlayer(drawCard: false);
    }

    private bool AnyAliveOpponentFor(ulong clientId)
    {
        bool side = LobbyTeamManager.IsClientOnPlayerSide(clientId);
        return allCharacters.Any(c => c != null && c.IsAlive && c.isPlayer != side);
    }

    /// <summary>
    /// Oyuncunun yapabileceği anlamlı bir hamle var mı? Oynanmamış kartı ve boş hücre varsa kart koyabilir;
    /// ya da canlı karakteri ve saldırılacak canlı bir düşman varsa saldırabilir.
    /// </summary>
    private bool PlayerHasMeaningfulAction(ulong clientId)
    {
        bool side = LobbyTeamManager.IsClientOnPlayerSide(clientId);

        if (CountRemainingCards(clientId) > 0 && CountFreeCells(side) > 0)
            return true;

        bool hasOwnAlive = allCharacters.Any(c => c != null && c.IsAlive && c.ControllerClientId == clientId);
        bool hasAliveEnemy = allCharacters.Any(c => c != null && c.IsAlive && c.isPlayer != side);
        return hasOwnAlive && hasAliveEnemy;
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

        // Yapacak hiçbir şeyi kalmamış oyuncuyu (kartı da canlı karakteri de yok) süre
        // dolmasını bekletmeden atla. Savaş fazında çalışır; yerleşimde herkes sırasını alır.
        for (int attempt = 0; attempt < matchPlayers.Count; attempt++)
        {
            activePlayerIndex = (activePlayerIndex + 1) % matchPlayers.Count;
            if (activePlayerIndex == 0 && Phase.Value == BattlePhase.PlayerTurn)
                AdvanceRound();

            if (Phase.Value != BattlePhase.PlayerTurn ||
                PlayerHasMeaningfulAction(matchPlayers[activePlayerIndex]))
                break;

            Debug.Log($"[FLOW] Client {matchPlayers[activePlayerIndex]} yapacak hamlesi olmadığı için atlandı.");
        }

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

    private double ServerNow =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

    private void ResetTurnTimer()
    {
        TurnDeadline.Value = turnTimeLimitSeconds > 0f
            ? ServerNow + turnTimeLimitSeconds
            : 0d;
    }

    void Update()
    {
        UpdateLocalActionButtons();
        UpdateTurnTimerUI();

        if (!IsServer || !IsSpawned || battleEnded) return;

        if (Phase.Value == BattlePhase.WaitingForDecks)
        {
            if (deckSubmitTimeoutSeconds > 0f &&
                deckWaitStartTime >= 0f &&
                Time.unscaledTime - deckWaitStartTime >= deckSubmitTimeoutSeconds)
            {
                HandleDeckSubmitTimeout();
            }
            return;
        }

        if (turnTimeLimitSeconds <= 0f) return;
        if (Phase.Value != BattlePhase.InitialPlacement && Phase.Value != BattlePhase.PlayerTurn) return;
        if (ActivePlayerClientId.Value == ulong.MaxValue) return;
        if (TurnDeadline.Value <= 0d || ServerNow < TurnDeadline.Value) return;

        HandleAfkTimeout();

        // Güvenlik: tur ilerletilemediyse (ör. oyuncu listesi boş) süre her karede tetiklenmesin.
        if (!battleEnded && TurnDeadline.Value > 0d && ServerNow >= TurnDeadline.Value)
            ResetTurnTimer();
    }

    private void EnsureTurnTimerText()
    {
        if (turnTimerText != null)
            return;

        Canvas targetCanvas = BattleCanvasUtility.FindScreenCanvas(gameObject.scene);

        if (targetCanvas == null)
        {
            if (!timerCanvasWarned)
            {
                timerCanvasWarned = true;
                Debug.LogWarning("[TURN TIMER] Uygun ekran Canvas'ı bulunamadı; bulunana kadar yeniden denenecek.");
            }
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
        {
            // Canvas Start sırasında hazır olmayabilir; saniyede en fazla bir kez yeniden dene.
            if (Time.unscaledTime >= nextTimerCanvasSearchTime)
            {
                nextTimerCanvasSearchTime = Time.unscaledTime + 1f;
                EnsureTurnTimerText();
            }

            if (turnTimerText == null)
                return;
        }

        bool timerActive =
            !battleEnded &&
            turnTimeLimitSeconds > 0f &&
            TurnDeadline.Value > 0d &&
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

        int seconds = Mathf.CeilToInt(TurnTimeRemainingSeconds);
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
        if (actionSelectionUI != null)
            return;

        if (NetworkManager.Singleton == null) return;

        bool myTurn = IsClient &&
                      Phase.Value == BattlePhase.PlayerTurn &&
                      ActivePlayerClientId.Value == NetworkManager.Singleton.LocalClientId;

        if (attackButton != null) attackButton.interactable = myTurn;
        if (skillButton != null) skillButton.interactable = myTurn;
    }

    /// <summary>
    /// Süre dolduğunda oyuncunun yalnızca turu atlanır. Art arda kaç kez olursa olsun
    /// mağlup sayılmaz. Yerleşim fazında süre dolarsa o oyuncu kartsız tamamlanmış sayılır
    /// (kartlarını savaş fazında yerleştirmeye devam edebilir).
    /// </summary>
    private void HandleAfkTimeout()
    {
        ulong timedOutClient = ActivePlayerClientId.Value;

        Debug.LogWarning($"[BM] Süre aşımı: client {timedOutClient} turunu kaçırdı; sıra bir sonraki oyuncuya geçiyor.");
        WarnClient(timedOutClient, "Süren doldu, turun atlandı.");

        if (Phase.Value == BattlePhase.InitialPlacement)
        {
            CompleteInitialPlacementFor(timedOutClient);
            return;
        }

        EndTurn();
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Teslim Olma

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

    #region Maç Sonu / Ödül

    private void EndBattle(bool winnerIsPlayerSide, string reason)
    {
        FinishBattle(winnerIsPlayerSide, reason);
    }

    /// <summary>Kazananı olmayan (iptal) maç sonu.</summary>
    private void AbortBattle(string reason)
    {
        FinishBattle(null, reason);
    }

    /// <summary>Oynanış olmadan biten maçlarda (deste sorunları) XP verilmez.</summary>
    private static bool IsRewardableEnd(string reason)
    {
        return reason != "deck_invalid" && reason != "deck_timeout";
    }

    private static string BuildResultText(bool won, bool rewardable)
    {
        if (!rewardable)
            return "Maç başlayamadı: deste doğrulanamadı.";

        return won
            ? "Maç kazanıldı. Tecrübe kazandın."
            : "Maç kaybedildi. Kısmi tecrübe kazandın.";
    }

    private void FinishBattle(bool? winnerIsPlayerSide, string reason)
    {
        if (battleEnded) return;
        battleEnded = true;
        Phase.Value = BattlePhase.Finished;
        ActivePlayerClientId.Value = ulong.MaxValue;
        TurnDeadline.Value = 0d;

        bool rewardable = IsRewardableEnd(reason);

        // Sezon/casual ve takım boyutu bilgisini host bilir; joiner'ın MatchContext'i hiç ayarlanmadığı
        // için (maça katılırken bu bilgiyi kimse göndermiyor) sonuçla birlikte her oyuncuya sunucu yollar.
        bool seasonMatch = MatchContext.LastMatchWasSeason;
        int matchTeamSize = Mathf.Clamp(
            MatchContext.LastMatchTeamSize > 0
                ? MatchContext.LastMatchTeamSize
                : LobbyTeamManager.SelectedPlayersPerTeam,
            1, 3);

        Debug.Log($"[BM] Maç sona erdi (maç {currentMatchId}). Kazanan: " +
                  $"{(winnerIsPlayerSide.HasValue ? (winnerIsPlayerSide.Value ? "player" : "enemy") : "yok")} | sebep: {reason}");

        // Kazanma/kaybetme kararını SUNUCU verir; her oyuncuya yalnızca kendi sonucu gönderilir.
        // (Eskiden her client kararı kendi hesaplıyor ve herkese açık bir metotla ödülü veriyordu.)
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            ulong clientId = client.ClientId;
            bool won = winnerIsPlayerSide.HasValue &&
                       LobbyTeamManager.IsClientOnPlayerSide(clientId) == winnerIsPlayerSide.Value;

            NotifyMatchEndClientRpc(won, reason, rewardable, currentMatchId, seasonMatch, matchTeamSize, SendTo(clientId));
        }
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

        if (isPlayerTurn)
        {
            var enemyCards = StartBattleManager.Instance?.enemyMatchCards;
            if (enemyCards != null && enemyCards.Count > 0)
            {
                var randomEnemyCard = enemyCards[Random.Range(0, enemyCards.Count)];
                SpawnCharacter(randomEnemyCard, false);
            }
        }

        // ✅ saldırı çözümlemesi (VFX dahil)
        ResolveAttack(isPlayerTurn);

        EndTurn();
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

        // Maç bittiyse kalan parçalar sessizce yok sayılır.
        if (battleEnded)
            return;

        // Deste yalnızca "deste bekleme" fazında kabul edilir; aksi halde maç ortasında
        // deste sıfırlanıp ilk yerleşim yeniden başlatılabilirdi.
        if (Phase.Value != BattlePhase.WaitingForDecks)
        {
            RejectAction(senderId, "Deste parçası", "Deste gönderimi artık kabul edilmiyor.");
            return;
        }

        if (totalCards < BattleRules.MinimumDeckSize || totalCards > BattleRules.DeckSize ||
            cardIndex < 0 || cardIndex >= totalCards)
        {
            FailDeckSubmission(senderId, $"geçersiz parça bilgisi ({cardIndex}/{totalCards}).");
            return;
        }

        if (cardIndex == 0 || !playerSubmittedCards.ContainsKey(senderId))
        {
            playerSubmittedCards[senderId] = new List<CardData>();
            playerSubmittedCardIds[senderId] = new List<string>();
            playerSubmittedJtis[senderId] = new HashSet<string>();
            expectedSubmittedCardCounts[senderId] = totalCards;
            playedCardIndices.Remove(senderId);
        }

        if (expectedSubmittedCardCounts[senderId] != totalCards)
        {
            FailDeckSubmission(senderId, "deste boyutu gönderim ortasında değişti.");
            return;
        }

        string proof = cardProof.ToString();
        if (!TryDecodeSubmittedCard(senderId, proof, out CardData card, out string identity, out string error))
        {
            FailDeckSubmission(senderId, $"kart {cardIndex} reddedildi: {error}");
            return;
        }

        if (allowUnsignedTestDeck && identity.StartsWith("TEST:"))
            identity = $"{identity}:SLOT:{cardIndex}";

        HashSet<string> identities = playerSubmittedJtis[senderId];
        if (!identities.Add(identity))
        {
            FailDeckSubmission(senderId, "aynı fiziksel kart destede tekrar gönderildi.");
            return;
        }

        if (!allowUnsignedTestDeck && !identity.StartsWith("TEST:"))
        {
            foreach (var pair in playerSubmittedJtis)
            {
                if (pair.Key != senderId && pair.Value != null && pair.Value.Contains(identity))
                {
                    identities.Remove(identity);
                    FailDeckSubmission(senderId, "aynı fiziksel kart iki oyuncu tarafından gönderildi.");
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

    [ServerRpc(RequireOwnership = false)]
    public void SubmitDeckServerRpc(
        FixedString4096Bytes[] signedTokenArray,
        ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (battleEnded)
            return;

        if (Phase.Value != BattlePhase.WaitingForDecks)
        {
            RejectAction(senderId, "Deste", "Deste gönderimi artık kabul edilmiyor.");
            return;
        }

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
                return;
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
        playedCardIndices.Remove(senderId);

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

    private void SpawnCharactersFromSubmittedDecks()
    {
        var players = new List<ulong>(playerSubmittedCards.Keys);
        int expectedPlayers = Mathf.Clamp(
            LobbyTeamManager.SelectedPlayersPerTeam * 2,
            2,
            6);

        if (players.Count < expectedPlayers)
        {
            BroadcastWarning($"{expectedPlayers} oyuncu doğrulanmış deste göndermeden savaş başlayamaz.");
            return;
        }

        foreach (ulong playerId in players)
        {
            List<CardData> deck = playerSubmittedCards[playerId];
            if (deck == null || deck.Count < BattleRules.MinimumDeckSize)
            {
                BroadcastWarning(
                    $"Client {playerId} destesinden en az {BattleRules.MinimumDeckSize} kart göndermeli.");
                return;
            }
        }

        int playerSideCount = players.Count(id => LobbyTeamManager.IsClientOnPlayerSide(id));
        int enemySideCount = players.Count - playerSideCount;
        if (playerSideCount != LobbyTeamManager.SelectedPlayersPerTeam ||
            enemySideCount != LobbyTeamManager.SelectedPlayersPerTeam)
        {
            BroadcastWarning(
                $"Takım dağılımı geçersiz. Ay={playerSideCount}, Kan={enemySideCount}, " +
                $"beklenen={LobbyTeamManager.SelectedPlayersPerTeam}.");
            return;
        }

        BroadcastWarning(string.Empty);

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

        if (!IsSendersTurn(senderId, out string turnReason, combatOnly: true))
        {
            RejectAction(senderId, "Seçili saldırı", turnReason);
            return;
        }

        if (NetworkManager.Singleton == null ||
            NetworkManager.Singleton.SpawnManager == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                attackerNetworkObjectId, out NetworkObject attackerObject) ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                targetNetworkObjectId, out NetworkObject targetObject))
        {
            RejectAction(senderId, "Seçili saldırı", "Karakter bulunamadı.");
            return;
        }

        Character attacker = attackerObject.GetComponent<Character>();
        Character target = targetObject.GetComponent<Character>();

        if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive)
        {
            RejectAction(senderId, "Seçili saldırı", "Saldırgan/hedef geçersiz veya düşmüş.");
            return;
        }

        if (!IsSenderAuthorizedForCharacter(senderId, attacker))
        {
            RejectAction(senderId, "Seçili saldırı", "Bu karakter üzerinde yetkin yok.");
            return;
        }

        if (!attacker.CanUseNormalAction)
        {
            RejectAction(senderId, "Seçili saldırı", "Karakter sersemletildi; saldıramaz.");
            return;
        }

        if (attacker.isPlayer == target.isPlayer)
        {
            RejectAction(senderId, "Seçili saldırı", "Takım arkadaşına saldırılamaz.");
            return;
        }

        BattleGrid targetGrid = GetGridFor(target.isPlayer);
        Character allowedTarget = targetGrid?.ResolveTarget(
            target.gridColumn,
            AbilityTargetingMode.Normal);

        if (allowedTarget != target)
        {
            RejectAction(senderId, "Seçili saldırı",
                $"{target.cardData?.cardName} ön sıradaki karakter tarafından korunuyor.");
            return;
        }

        BroadcastBasicAttackFeedback(attacker, target);
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

        if (!IsSendersTurn(senderId, out string reason, combatOnly: true))
        {
            RejectAction(senderId, "Saldırı", reason);
            return;
        }

        var attacker = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.CanUseNormalAction &&
            c.isPlayer == senderIsPlayerSide &&
            (c.ControllerClientId == ulong.MaxValue || c.ControllerClientId == senderId));
        if (attacker == null)
        {
            RejectAction(senderId, "Saldırı", "Saldırabilecek canlı karakterin yok.");
            return;
        }

        int targetColumn = ResolveLegacyTargetColumn(attacker, AbilityTargetingMode.Normal);
        if (ResolveColumnAttack(attacker, targetColumn, AbilityTargetingMode.Normal))
            EndTurn();
        else
            RejectAction(senderId, "Saldırı", "Geçerli bir hedef yok.");
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

        if (!IsSendersTurn(senderId, out string turnErrorReason, combatOnly: true))
        {
            RejectAction(senderId, "Yetenek", turnErrorReason);
            return;
        }

        var character = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer == senderIsPlayerSide &&
            (c.ControllerClientId == ulong.MaxValue || c.ControllerClientId == senderId));
        if (character == null)
        {
            RejectAction(senderId, "Yetenek", "Yetenek kullanabilecek canlı karakterin yok.");
            return;
        }

        IActiveAbility activeAbility = AbilityCatalog.GetActive(character.cardData?.ability);
        AbilityTargetingMode targetingMode = activeAbility != null
            ? activeAbility.TargetingMode
            : AbilityTargetingMode.Normal;
        int targetColumn = GetAbilityTargetSide(activeAbility) == AbilityTargetSide.Enemy
            ? ResolveLegacyTargetColumn(character, targetingMode)
            : Mathf.Max(0, character.gridColumn);

        if (!TryUseActiveAbilityForCharacter(character, targetColumn, senderId, out string abilityReason))
        {
            RejectAction(senderId, $"Yetenek ({character.cardData?.cardName})", abilityReason);
        }
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

        if (!TryFindUnplayedCard(senderClientId, cardId, out CardData card, out int cardIndex,
                out string cardReason, out bool cardAlreadyPlayed))
        {
            RejectAction(senderClientId, "Kart oynama", cardReason);

            if (cardAlreadyPlayed)
                DiscardSpentCardClientRpc(cardId, SendTo(senderClientId));
            return;
        }

        if (!IsSendersTurn(senderClientId, out string turnReason))
        {
            RejectAction(senderClientId, "Kart oynama", turnReason);
            return;
        }

        if (Phase.Value == BattlePhase.PlayerTurn && placedThisTurn.Contains(senderClientId))
        {
            RejectAction(senderClientId, "Kart oynama", "Bu turda zaten bir kart yerleştirdin.");
            return;
        }

        if (!HasFreeSpawnSlot(isPlayer, out string slotReason))
        {
            RejectAction(senderClientId, "Kart oynama", slotReason);
            return;
        }

        Character spawned = SpawnCharacter(card, isPlayer, senderClientId);
        if (spawned == null)
        {
            // Başarısız spawn "kart oynandı" sayılmaz; kart destede kalır.
            RejectAction(senderClientId, "Kart oynama", "Karakter sahaya yerleştirilemedi (boş yer yok).");
            return;
        }

        MarkCardPlayed(senderClientId, cardIndex);
        OnCardPlaced(senderClientId);
    }

    #endregion
    // ---------------------------------------------------------------------

    #region Victory

    /// <summary>
    /// Bir taraf, sahadaki TÜM karakterleri düşmüşse VE destesindeki/elindeki sahaya
    /// konabilecek hiç oynanmamış kartı kalmamışsa elenir. Karakterleri ölse bile kartı
    /// kalan taraf yeni kart koyarak savaşa devam eder. (Saha düşmüşlerle dolduysa kalan
    /// kartlar yerleştirilemeyeceği için sayılmaz.)
    /// </summary>
    public void CheckVictory()
    {
        if (!IsServer || battleEnded)
            return;

        // Deste bekleme / bitmiş fazlarda saha boştur; "iki taraf da elendi" sanılmasın.
        if (Phase.Value != BattlePhase.InitialPlacement && Phase.Value != BattlePhase.PlayerTurn)
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

        bool playerEliminated = playerAlive == 0 && CountPlayableRemainingCardsForSide(true) == 0;
        bool enemyEliminated = enemyAlive == 0 && CountPlayableRemainingCardsForSide(false) == 0;

        if (!playerEliminated && !enemyEliminated)
            return;

        if (playerEliminated && enemyEliminated)
        {
            // Son karakterler aynı hamlede düştü: hamleyi yapan taraf kazanır (maç kilitlenmesin).
            bool actingSideIsPlayer =
                ActivePlayerClientId.Value != ulong.MaxValue &&
                LobbyTeamManager.IsClientOnPlayerSide(ActivePlayerClientId.Value);
            EndBattle(actingSideIsPlayer, "victory_simultaneous");
            return;
        }

        EndBattle(enemyEliminated, "victory");
    }

    [ClientRpc]
    private void NotifyMatchEndClientRpc(
        bool localWon,
        string reason,
        bool rewardable,
        string matchId,
        bool seasonMatch,
        int teamSize,
        ClientRpcParams clientRpcParams = default)
    {
        ulong localId = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClientId
            : NetworkManager.ServerClientId;

        Debug.Log($"[BM] Maç sonu bildirimi (client {localId}): " +
                  $"{(localWon ? "KAZANDIN" : "KAYBETTİN")} (sebep: {reason})");

        if (rewardable)
        {
            // MatchEndXPManager sezon puanını MatchContext'ten okur; sunucudan gelen değeri yaz.
            MatchContext.Set(seasonMatch, teamSize);
            ApplyServerVerdictReward(localWon, matchId);
        }

        MatchEndUI.Show(localWon, reason, BuildResultText(localWon, rewardable));
    }

    /// <summary>
    /// Sunucunun verdiği sonucu bu cihazdaki XP yöneticisine uygular. Metot artık public değil,
    /// karar client'ta hesaplanmıyor ve aynı maç için ikinci kez çalışmaz.
    /// </summary>
    private void ApplyServerVerdictReward(bool won, string matchId)
    {
        if (!string.IsNullOrEmpty(matchId))
        {
            if (matchId == lastRewardedMatchId)
                return;
            lastRewardedMatchId = matchId;
        }

        var deckManager = Object.FindFirstObjectByType<DeckManagerObject>();
        var usedCards = deckManager != null ? deckManager.currentMatchDeck : null;

        var xpManager = Object.FindFirstObjectByType<MatchEndXPManager>();

        if (xpManager != null)
        {
            xpManager.GrantMatchRewards(won, usedCards);
        }

        Debug.Log(won ? "Kazandın! Ödül verildi." : "Kaybettin. Az da olsa XP verildi.");
    }

    #endregion

    private string GetCharacterVFXKey(Character character)
    {
        if (character == null)
            return string.Empty;

        GameObject prefab = character.cardData != null
            ? ResolvePrefab(character.cardData)
            : null;

        string characterName = prefab != null
            ? prefab.name
            : character.gameObject.name;

        return characterName.Replace("(Clone)", "").Trim();
    }
    private Vector3 GetCharacterVFXPosition(Character character)
    {
        if (character == null)
            return Vector3.zero;

        foreach (Transform child in character.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "VFXAnchor")
                return child.position;
        }

        return character.transform.position;
    }
    private GameObject ResolvePrefab(CardData card)
    {
        if (card == null)
            return null;

        if (!string.IsNullOrEmpty(card.prefab))
        {
            var res = Resources.Load<GameObject>(card.prefab);
            if (res != null) return res;
            Debug.LogWarning($"[BM] Resources prefab bulunamadı: {card.prefab}  (Kart: {card.cardName})");
        }

        if (card.characterPrefab3D != null) return card.characterPrefab3D;

        GameObject debugPrefab = BattleDebugPrefabBootstrap.GetPrefab();

        if (forceDebugCharacterVisuals && debugPrefab != null)
        {
            Debug.Log($"[DEBUG PREFAB] {card?.cardName} görünür test kapsülüyle spawn ediliyor.");
            return debugPrefab;
        }

        if (characterPrefab != null) return characterPrefab;

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

        if (!TryResolveSenderSide(senderId, out bool isPlayer))
        {
            RejectPlacement(senderId, "Oyuncu tanınamadı.");
            return;
        }

        if (!TryFindUnplayedCard(senderId, cardName, out CardData card, out int cardIndex,
                out string cardReason, out bool cardAlreadyPlayed))
        {
            RejectPlacement(senderId, cardReason);

            // Sunucu bu kartı harcanmış sayıyor ama oyuncunun elinde hâlâ duruyor: elden çıkar.
            if (cardAlreadyPlayed)
                DiscardSpentCardClientRpc(cardName, SendTo(senderId));
            return;
        }

        if (!IsSendersTurn(senderId, out string turnReason))
        {
            RejectPlacement(senderId, turnReason);
            return;
        }

        if (Phase.Value == BattlePhase.PlayerTurn && placedThisTurn.Contains(senderId))
        {
            RejectPlacement(senderId, "Bu turda zaten bir kart yerleştirdin.");
            return;
        }

        if (!HasFreeSpawnSlot(isPlayer, out string slotReason))
        {
            RejectPlacement(senderId, slotReason);
            return;
        }

        Collider zone = isPlayer ? playerZone : enemyZone;

        if (zone == null)
        {
            Debug.LogError("❌ PlayerZone/EnemyZone Collider atanmadı!");
            RejectPlacement(senderId, "Saha bölgesi hazır değil.");
            return;
        }

        Debug.Log($"Spawn isteyen senderId: {senderId}, isPlayer: {isPlayer}, seçilen zone: {zone.name}, position: {position}");
        Bounds b = zone.bounds;

        bool insideXZ =
            position.x >= b.min.x && position.x <= b.max.x &&
            position.z >= b.min.z && position.z <= b.max.z;

        if (!insideXZ)
        {
            RejectPlacement(senderId, "Kartı kendi bölgene yerleştirmelisin.");
            return;
        }

        // Grid kontrolü Instantiate'ten ÖNCE: hücre dolu ise hiçbir şey spawn edilmez.
        bool frontIsMaxZ = isPlayer ? playerFrontRowIsMaxZ : enemyFrontRowIsMaxZ;
        var (cellRow, cellCol) = BattleGrid.ResolveCellFromWorldPosition(b, position, gridColumns, frontIsMaxZ);

        string cellBlockReason = GetCellBlockReason(isPlayer, cellRow, cellCol);
        if (cellBlockReason != null)
        {
            RejectPlacement(senderId, cellBlockReason);
            return;
        }

        var prefabToUse = ResolvePrefab(card);
        if (prefabToUse == null)
        {
            Debug.LogError($"[BM] Prefab yok: {card?.cardName}");
            RejectPlacement(senderId, "Karakter modeli bulunamadı.");
            return;
        }

        // Görsel konum, grid hücresiyle birebir uyuşsun diye hücre merkezine hizala.
        Vector3 placePosition = position;
        if (snapPlacementToCellCenter &&
            TryGetCellWorldCenter(b, cellRow, cellCol, frontIsMaxZ, position.y, out Vector3 cellCenter))
        {
            placePosition = cellCenter;
        }

        Vector3 spawnPos = placePosition + Vector3.up * 0.4f;

        Quaternion spawnRot = isPlayer
            ? Quaternion.Euler(0f, 0f, 0f)
            : Quaternion.Euler(0f, 180f, 0f);

        var obj = Instantiate(prefabToUse, spawnPos, spawnRot);
        var net = obj.GetComponent<NetworkObject>();
        var ch = obj.GetComponent<Character>();

        if (net == null || ch == null)
        {
            Destroy(obj);
            RejectPlacement(senderId, "Karakter prefabı geçersiz.");
            return;
        }

        net.Spawn();
        ch.SetPlayerSide(isPlayer);
        ch.Setup(card, senderId);

        if (!TryRegisterSpawnedCharacterByWorldPosition(ch, isPlayer, cellRow, cellCol))
        {
            // Grid hücreyi vermedi: karakteri gridsiz bırakmak yerine geri al.
            DiscardSpawned(obj);
            RejectPlacement(senderId, "Bu hücreye yerleştirilemedi. Başka bir hücre seç.");
            return;
        }

        Debug.Log($"[BM] Grid hücresi: {cellRow}/{cellCol}");
        Debug.Log($"✅ {card.cardName} spawn edildi → {(isPlayer ? "Oyuncu" : "Düşman")} bölgesi");

        MarkCardPlayed(senderId, cardIndex);
        OnCardPlaced(senderId);

        PlacementResultClientRpc(true, SendTo(senderId));
    }

    /// <summary>
    /// Sunucuya göre bu kart zaten harcanmış. Oyuncunun elinde hâlâ görünüyorsa
    /// (ör. önceki başarılı cevap kaybolduysa) elden çıkar ve yerleştirme modunu kapat.
    /// </summary>
    [ClientRpc]
    private void DiscardSpentCardClientRpc(string cardKey, ClientRpcParams clientRpcParams = default)
    {
        if (CharacterPlacer.Instance != null)
            CharacterPlacer.Instance.AbortPlacementForCard(cardKey);

        var hand = Object.FindFirstObjectByType<HandUIManager>(FindObjectsInactive.Include);
        if (hand != null)
            hand.DiscardCardsById(cardKey);
    }

    [ClientRpc]
    private void PlacementResultClientRpc(bool success, ClientRpcParams clientRpcParams = default)
    {
        if (CharacterPlacer.Instance != null)
            CharacterPlacer.Instance.PlacementResult(success);
    }

    // ---------------------------------------------------------
    // Eski prototip akışı için saldırı çözümü + VFX/SFX geri bildirimi
    // ---------------------------------------------------------
    private void ResolveAttack(bool isPlayerTurn)
    {
        if (!IsServer)
            return;

        Debug.Log($"⚔️ ResolveAttack başladı (PlayerTurn: {isPlayerTurn})");

        Character attacker = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer == isPlayerTurn);

        Character defender = allCharacters.FirstOrDefault(c =>
            c != null &&
            c.IsAlive &&
            c.isPlayer != isPlayerTurn);

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

        // Sunucu içinden çağrıldığı için HealthSystem'in yetki kontrolünden geçer.
        defenderHealth.TakeDamageServerRpc(damage);

        // Host dahil tüm istemcilerde VFX/SFX geri bildirimini oynat.
        PlayAttackFeedbackClientRpc(
            attackerVFXKey,
            defenderVFXKey,
            attackPosition,
            hitPosition);

        Debug.Log($"⚔️ {attacker.cardData.cardName} → {defender.cardData.cardName} | -{damage} HP");
    }

    [ClientRpc]
    private void PlayAttackFeedbackClientRpc(
        string attackerVFXKey,
        string defenderVFXKey,
        Vector3 attackPosition,
        Vector3 hitPosition)
    {
        // Saldıran karakterin Attack efekti.
        if (VFXManager.Instance != null)
        {
            VFXManager.Instance.PlayCharacterVFX(
                attackerVFXKey,
                "Attack",
                attackPosition);

            Debug.Log($"⚡ Attack VFX: {attackerVFXKey} @ {attackPosition}");
        }

        // Hedefin "Hit" efekti HealthSystem.ShowDamageClientRpc içinde, hasar uygulandığında oynanır.

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(basicAttackSfxId))
            AudioManager.Instance.PlaySFX(basicAttackSfxId);
    }

} // <--- Bu parantez tüm BattleManager sınıfını kapatır.
