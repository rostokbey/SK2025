using System;
using UnityEngine;
using Unity.Netcode;

public class CharacterPlacer : MonoBehaviour
{
    public static CharacterPlacer Instance;

    public LayerMask battlefieldLayer; // Inspector'dan Battlefield layer'ını seçeceğiz

    private Camera mainCamera;
    private CardData cardToPlace;
    private Action onPlacedCallback;  // ✅ Spawn sonrası callback
    private bool isPlacementMode = false;

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

        if (Input.GetMouseButtonDown(0))
        {
            PlaceCharacter();
        }

        if (Input.GetMouseButtonDown(1))
        {
            isPlacementMode = false;
            cardToPlace = null;
            onPlacedCallback = null;
            Debug.Log("Yerleştirme iptal edildi.");
        }
    }

    // ✅ Callback destekli EnterPlacementMode
    public void EnterPlacementMode(CardData card, Action onPlaced)
    {
        if (card == null) return;
        cardToPlace = card;
        onPlacedCallback = onPlaced;
        isPlacementMode = true;
        Debug.Log(card.cardName + " için yerleştirme modu aktif.");
    }

    private void PlaceCharacter()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, battlefieldLayer))
        {
            Vector3 placementPosition = hit.point;

            // BattleManager'a spawn isteği gönder
            BattleManager.Instance.SpawnCharacterAtPositionServerRpc(cardToPlace.id, placementPosition);

            // Callback çalıştır
            onPlacedCallback?.Invoke();

            // Yerleştirme modundan çık
            isPlacementMode = false;
            cardToPlace = null;
            onPlacedCallback = null;
        }
        else
        {
            Debug.Log("Geçersiz yer. Lütfen savaş alanına tıklayın.");
        }
    }
}
