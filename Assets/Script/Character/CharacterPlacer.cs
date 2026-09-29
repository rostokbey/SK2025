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
        if (!isPlacementMode) return;
        if (isWaitingServerResult) return;

        if (Input.GetMouseButtonDown(0))
        {
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

        cardToPlace = card;
        onPlacedCallback = onPlaced;
        isPlacementMode = true;
        isWaitingServerResult = false;

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
        isWaitingServerResult = true;
        BattleManager.Instance.SpawnCharacterAtPositionServerRpc(cardToPlace.id, placementPosition);
    }

    private bool TryResolvePlacementFromSideZone(Ray ray, out Vector3 position)
    {
        position = Vector3.zero;
        if (BattleManager.Instance == null || NetworkManager.Singleton == null)
            return false;

        bool hostSide = NetworkManager.Singleton.LocalClientId == NetworkManager.ServerClientId;
        Collider zone = hostSide ? BattleManager.Instance.playerZone : BattleManager.Instance.enemyZone;
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
        isWaitingServerResult = false;

        if (success)
        {
            onPlacedCallback?.Invoke();

            isPlacementMode = false;
            cardToPlace = null;
            onPlacedCallback = null;

            Debug.Log("✅ Yerleştirme başarılı, kart kullanıldı.");
        }
        else
        {
            Debug.LogWarning("❌ Yerleştirme başarısız, kart elde kalacak.");
        }
    }

    private void CancelPlacement()
    {
        isPlacementMode = false;
        isWaitingServerResult = false;
        cardToPlace = null;
        onPlacedCallback = null;

        Debug.Log("Yerleştirme iptal edildi.");
    }
}