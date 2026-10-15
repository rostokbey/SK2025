using System;
using UnityEngine;
using Unity.Netcode;

public class CharacterPlacer : MonoBehaviour
{
    public static CharacterPlacer Instance;

    [Header("Placement")]
    public LayerMask battlefieldLayer;
    public float spawnYOffset = 0.6f;
    [Tooltip("Eski sahne uyumluluğu; normalde 0 kalmalı.")]
    public float spawnZOffset = 0f;

    private Camera mainCamera;
    private CardData cardToPlace;
    private Action onPlacedCallback;
    private bool isPlacementMode = false;
    private bool isWaitingServerResult = false;
    private int placementEnteredFrame = -1;

    // Sunucuya gönderilmiş ve henüz cevaplanmamış isteğin sahibi. Cevap, o an seçili olan
    // karta değil BU isteğe uygulanır; aksi halde cevap beklerken başka karta geçilirse
    // ya da sağ tıkla iptal edilirse yanlış kart elden silinir / hiç silinmez.
    private CardData pendingCard;
    private Action pendingCallback;
    private float waitStartedAt;
    private const float ServerResultTimeoutSeconds = 5f;

    public bool IsPlacementMode => isPlacementMode;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        // Cevap hiç gelmezse arayüz sonsuza kadar kilitli kalmasın.
        if (isWaitingServerResult &&
            Time.unscaledTime - waitStartedAt > ServerResultTimeoutSeconds)
        {
            Debug.LogWarning("[PLACEMENT] Sunucu cevabı zaman aşımına uğradı; istek iptal sayıldı.");
            isWaitingServerResult = false;
            pendingCard = null;
            pendingCallback = null;
        }

        if (!isPlacementMode) return;
        if (isWaitingServerResult) return;

        if (Input.GetMouseButtonDown(0))
        {
            // Kartı seçen tıklama aynı karede sahaya da tıklama sayılmasın.
            if (Time.frameCount == placementEnteredFrame)
                return;

            // Savaş sahnesindeki tam ekran UI arka planı her noktayı "UI" saydığı
            // için EventSystem.IsPointerOverGameObject kullanılamaz. Yalnızca
            // gerçekten açık olan ayarlar menüsü tıklamayı engeller.
            GameObject settingsPanel = GameObject.Find("BattleSettingsPanel");
            if (settingsPanel == null || !settingsPanel.activeInHierarchy)
                PlaceCharacter();
        }

        if (Input.GetMouseButtonDown(1))
        {
            CancelPlacement();
        }
    }

    public void EnterPlacementMode(CardData card, Action onPlaced)
    {
        if (card == null) return;

        // Sunucudan cevap beklenirken başka karta geçilemez.
        if (isWaitingServerResult)
        {
            Debug.LogWarning("[PLACEMENT] Önceki yerleştirmenin sunucu cevabı bekleniyor; kart değiştirilemedi.");
            return;
        }

        cardToPlace = card;
        onPlacedCallback = onPlaced;
        isPlacementMode = true;
        placementEnteredFrame = Time.frameCount;

        Debug.Log(card.cardName + " için yerleştirme modu aktif.");
    }

    private void PlaceCharacter()
    {
        if (cardToPlace == null)
        {
            Debug.LogWarning("Yerleştirilecek kart yok.");
            return;
        }

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("❌ Main Camera bulunamadı! Kameranın Tag değeri MainCamera olmalı.");
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        Debug.Log(
            $"🖱️ Tıklama Screen Pos: {Input.mousePosition} | " +
            $"Ray Origin: {ray.origin} | Ray Direction: {ray.direction}"
        );

        // Önce her şeye ray atalım, gerçekten neye çarpıyor görelim
        if (Physics.Raycast(ray, out RaycastHit debugHit, 5000f))
        {
            Debug.Log(
                $"🎯 DEBUG HIT: {debugHit.collider.name} | " +
                $"Layer: {LayerMask.LayerToName(debugHit.collider.gameObject.layer)} | " +
                $"World Pos: {debugHit.point}"
            );
        }
        else
        {
            Debug.LogWarning("❌ DEBUG: Ray hiçbir objeye çarpmadı.");
        }

        Vector3 placementPosition;

        // Öncelik gerçek Battlefield collider/layer raycast'idir. Sahne zemini
        // collider taşımıyorsa BattleManager'daki taraf alanına yatay düzlem
        // raycast'i uygulanır; böylece yalnızca görsel 3D zeminle de test edilir.
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, battlefieldLayer))
        {
            placementPosition = hit.point + Vector3.up * spawnYOffset;
            Debug.Log($"✅ BATTLEFIELD HIT: {hit.collider.name} | Spawn Pos: {hit.point}");
        }
        else if (!TryResolvePlacementFromSideZone(ray, out placementPosition))
        {
            Debug.LogWarning("❌ Geçersiz yer: Battlefield layer ve oyuncu alanı bulunamadı.");
            return;
        }

        Debug.Log($"📍 Karakter spawn konumu: {placementPosition}");
        // İsteği ve ait olduğu kartı/callback'i gönderilmeden ÖNCE sakla. Host'ta RPC aynı çağrı
        // içinde işlenip PlacementResult hemen çağrılabildiği için sıra önemli.
        pendingCard = cardToPlace;
        pendingCallback = onPlacedCallback;
        isWaitingServerResult = true;
        waitStartedAt = Time.unscaledTime;
        BattleManager.Instance.SpawnCharacterAtPositionServerRpc(cardToPlace.id, placementPosition);
    }

    private bool TryResolvePlacementFromSideZone(Ray ray, out Vector3 position)
    {
        position = Vector3.zero;
        if (BattleManager.Instance == null || NetworkManager.Singleton == null)
            return false;

        bool playerSide = LobbyTeamManager.IsClientOnPlayerSide(
            NetworkManager.Singleton.LocalClientId);
        Collider zone = playerSide
            ? BattleManager.Instance.playerZone
            : BattleManager.Instance.enemyZone;
        if (zone == null)
            return false;

        Bounds bounds = zone.bounds;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, bounds.max.y, 0f));
        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3 point = ray.GetPoint(distance);
        const float margin = 0.05f;
        point.x = Mathf.Clamp(point.x, bounds.min.x + margin, bounds.max.x - margin);
        point.z = Mathf.Clamp(point.z, bounds.min.z + margin, bounds.max.z - margin);
        point.y = bounds.max.y + spawnYOffset;
        position = point;

        Debug.Log("[PLACEMENT] Battlefield collider kaçırıldı; taraf alanı düzlemi kullanıldı.");
        return true;
    }

    public void PlacementResult(bool success)
    {
        CardData resolvedCard = pendingCard;
        Action resolvedCallback = pendingCallback;

        isWaitingServerResult = false;
        pendingCard = null;
        pendingCallback = null;

        if (resolvedCard == null)
        {
            // Zaman aşımına uğramış / bilinmeyen isteğin geç gelen cevabı.
            Debug.LogWarning("[PLACEMENT] Hangi isteğe ait olduğu bilinmeyen yerleştirme cevabı yok sayıldı.");
            return;
        }

        if (success)
        {
            // Doğru kartı elden çıkarır (cevap beklenirken seçim değişmiş olsa bile).
            resolvedCallback?.Invoke();

            // Yerleştirme modunu yalnızca hâlâ AYNI kart için açıksa kapat.
            if (cardToPlace == resolvedCard)
            {
                isPlacementMode = false;
                cardToPlace = null;
                onPlacedCallback = null;
            }

            Debug.Log("✅ Yerleştirme başarılı, kart kullanıldı.");
        }
        else
        {
            Debug.LogWarning("❌ Yerleştirme başarısız, kart elde kalacak.");
        }
    }

    /// <summary>Sunucu bu kartı zaten harcanmış saydıysa o kart için yerleştirme modundan çık.</summary>
    public void AbortPlacementForCard(string cardId)
    {
        if (cardToPlace == null || string.IsNullOrEmpty(cardId)) return;
        if (!string.Equals(cardToPlace.id, cardId, StringComparison.OrdinalIgnoreCase)) return;

        isPlacementMode = false;
        cardToPlace = null;
        onPlacedCallback = null;
    }

    private void CancelPlacement()
    {
        // Sunucuya gönderilmiş bir istek varsa (pendingCard) cevabı yine doğru karta uygulanır;
        // sadece yerleştirme modu kapanır.
        isPlacementMode = false;
        cardToPlace = null;
        onPlacedCallback = null;

        Debug.Log("Yerleştirme iptal edildi.");
    }
}
