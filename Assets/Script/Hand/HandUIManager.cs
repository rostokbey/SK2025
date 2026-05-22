using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HandUIManager : MonoBehaviour
{
    [Header("UI Alanları")]
    public Transform handArea;
    public Button attackButton;

    [Header("Prefab")]
    public GameObject cardPrefab;

    [Header("Arka Plan")]
    public GameObject backgroundCard;

    private readonly List<CardUI> handCards = new();
    private Queue<CardData> remainingDeck;

    private const int MaxHandSize = 5;

    public void Init(List<CardData> deck)
    {
        if (handArea == null || cardPrefab == null || deck == null)
        {
            Debug.LogError("[HandUI] Gerekli referanslar eksik!");
            return;
        }

        foreach (Transform child in handArea)
            Destroy(child.gameObject);

        handCards.Clear();

        remainingDeck = new Queue<CardData>(deck);

        if (backgroundCard != null)
        {
            GameObject bg = Instantiate(backgroundCard, handArea);
            bg.transform.SetAsFirstSibling();
        }

        for (int i = 0; i < MaxHandSize; i++)
            DrawCard();

        Debug.Log($"[HandUI] Başlatıldı. Toplam deste: {deck.Count}, elde: {handCards.Count}");
    }

    public void DrawCard()
    {
        if (remainingDeck == null || remainingDeck.Count == 0)
            return;

        if (handCards.Count >= MaxHandSize)
            return;

        CardData card = remainingDeck.Dequeue();
        AddCardToHand(card);

        Debug.Log("[HandUI] Ele kart eklendi: " + card.cardName);
    }

    private void AddCardToHand(CardData card)
    {
        GameObject go = Instantiate(cardPrefab, handArea);
        CardUI ui = go.GetComponent<CardUI>();

        if (ui == null)
        {
            Debug.LogWarning("[HandUI] CardUI component yok!");
            Destroy(go);
            return;
        }

        ui.isInBattle = true;
        ui.SetCardData(card, true);

        ui.onCardClicked += clickedCardUI =>
        {
            CardData selectedCard = clickedCardUI.GetCardData();

            if (CharacterPlacer.Instance == null)
            {
                Debug.LogError("[HandUI] CharacterPlacer bulunamadı!");
                return;
            }

            CharacterPlacer.Instance.EnterPlacementMode(selectedCard, () =>
            {
                handCards.Remove(clickedCardUI);
                Destroy(clickedCardUI.gameObject);

                DrawCard();

                Debug.Log($"[HandUI] Kart oynandı: {selectedCard.cardName}. Elde kalan: {handCards.Count}");
            });
        };

        handCards.Add(ui);
    }
}