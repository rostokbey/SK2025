using System;
using UnityEngine;
using Unity.Netcode;

public class CharacterPlacer : MonoBehaviour
{
    public static CharacterPlacer Instance;

    [Header("Placement")]
    public LayerMask battlefieldLayer;
    public float spawnYOffset = 0.6f;
    public float spawnZOffset = -32.97f;

    private Camera mainCamera;
    private CardData cardToPlace;
    private Action onPlacedCallback;
    private bool isPlacementMode = false;
    private bool isWaitingServerResult = false;

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

        // Asıl yerleştirme raycast'i
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, battlefieldLayer))
        {
            Debug.Log(
                $"✅ BATTLEFIELD HIT: {hit.collider.name} | " +
                $"Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)} | " +
                $"Spawn Pos: {hit.point}"
            );

            Vector3 placementPosition = hit.point + Vector3.up * spawnYOffset;
            placementPosition.z += spawnZOffset;

            Debug.Log($"📍 Karakter spawn konumu: {placementPosition}");

            isWaitingServerResult = true;

            BattleManager.Instance.SpawnCharacterAtPositionServerRpc(cardToPlace.id, placementPosition);
        }
        else
        {
            Debug.LogWarning("❌ Geçersiz yer. Battlefield layer üzerinde bir yere tıklanmadı.");
        }
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