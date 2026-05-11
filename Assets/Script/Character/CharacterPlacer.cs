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
        {
            mainCamera = Camera.main;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 500f, battlefieldLayer))
        {
            Debug.Log($"Hit Object: {hit.collider.name}, Position: {hit.point}");


            Vector3 placementPosition = hit.point + Vector3.up * spawnYOffset;
            placementPosition.z += spawnZOffset;

            isWaitingServerResult = true;

            BattleManager.Instance.SpawnCharacterAtPositionServerRpc(cardToPlace.id, placementPosition);
        }
        else
        {
            Debug.Log("Geçersiz yer. Lütfen savaş alanına tıklayın.");
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