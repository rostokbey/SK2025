using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class HandUIManager : MonoBehaviour
{
    [Header("UI Alanları")]
    public Transform handArea;      // Kartların görüneceği panel (Horizontal Layout Group olmalı)
    public Button attackButton;     // Saldır butonu

    [Header("Prefab")]
    public GameObject cardPrefab;   // CardUI_Battle prefabı (üzerinde CardUI.cs)

    [Header("Arka Plan (opsiyonel)")]
    public GameObject backgroundCard; // Sadece bir tane arka kart resmi, opsiyonel

    private List<CardUI> handCards = new List<CardUI>();
    private CardData currentSelectedCard; // o tur seçilen kart

    // Deste kuyruğu
    private Queue<CardData> remainingDeck;
    private const int MaxHandSize = 5;

    // Başlatma -> Desteden ilk 5 kart çek
    public void Init(List<CardData> deck)
    {
        if (handArea == null || cardPrefab == null || deck == null)
        {
            Debug.LogError("[HandUI] Gerekli referanslar atanmamış!");
            return;
        }

        // Temizlik
        foreach (Transform child in handArea)
            Destroy(child.gameObject);
        handCards.Clear();
        currentSelectedCard = null;

        // Arka plan kartını oluştur (opsiyonel)
        if (backgroundCard != null)
        {
            var bgCard = Instantiate(backgroundCard, handArea);
            bgCard.transform.SetAsFirstSibling();
        }

        // Desteyi kuyruğa at
        remainingDeck = new Queue<CardData>(deck);

        // İlk 5 kartı çek
        for (int i = 0; i < MaxHandSize; i++)
            DrawCard();

        // Attack butonu
        if (attackButton != null)
        {
            attackButton.onClick.RemoveAllListeners();
            attackButton.onClick.AddListener(OnAttackPressed);
        }
        else
        {
            Debug.LogWarning("[HandUI] attackButton atanmamış!");
        }
    }

    /// <summary>
    /// Destenin sıradaki kartını elde boş slot varsa ekler
    /// </summary>
    public void DrawCard()
    {
        if (remainingDeck == null || remainingDeck.Count == 0)
            return;

        if (handCards.Count >= MaxHandSize)
            return;

        var nextCard = remainingDeck.Dequeue();
        AddCardToHand(nextCard);
    }

    /// <summary>
    /// Kart prefabını instantiate edip elde gösterir
    /// </summary>
    private void AddCardToHand(CardData card)
    {
        var go = Instantiate(cardPrefab, handArea);
        var ui = go.GetComponent<CardUI>();

        if (ui == null)
        {
            Debug.LogWarning("[HandUI] CardUI component yok!");
            return;
        }

        ui.isInBattle = true;
        ui.SetCardData(card, true);

        // Kart tıklanınca spawn işlemi
        ui.onCardClicked += (clickedCardUI) =>
        {
            var selectedCard = clickedCardUI.GetCardData();

            // CharacterPlacer ile yerleştirme moduna geç
            CharacterPlacer.Instance.EnterPlacementMode(selectedCard, () =>
            {
                // Spawn tamamlandıktan sonra kartı elden kaldır
                handCards.Remove(clickedCardUI);
                Destroy(clickedCardUI.gameObject);

                // Desteden yeni kart çek
                DrawCard();
            });
        };

        // isInBattle durumunda selectButton callback (istersen ek)
        ui.onSelect = (selectedCard) =>
        {
            currentSelectedCard = selectedCard;
            Debug.Log($"[HandUI] Kart seçildi: {selectedCard.cardName}");
        };

        handCards.Add(ui);
    }


    /// <summary>
    /// Attack butonuna basıldığında seçili kart spawn edilir, sonra elden çıkarılır
    /// </summary>
    private void OnAttackPressed()
    {
        if (currentSelectedCard == null)
        {
            Debug.LogWarning("[HandUI] Hiç kart seçilmedi.");
            return;
        }

        Debug.Log($"[HandUI] Attack basıldı: {currentSelectedCard.cardName}");

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.Attack(currentSelectedCard, true);

            // Kartı elden kaldır
            var uiToRemove = handCards.FirstOrDefault(c => c.GetCardData() == currentSelectedCard);
            if (uiToRemove != null)
            {
                handCards.Remove(uiToRemove);
                Destroy(uiToRemove.gameObject);
            }

            // ✅ Desteden yeni kart çek
            DrawCard();
        }
        else
        {
            Debug.LogError("[HandUI] BattleManager.Instance bulunamadı!");
        }

        currentSelectedCard = null;
    }
}