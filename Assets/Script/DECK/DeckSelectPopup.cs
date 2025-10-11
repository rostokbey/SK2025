using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DeckSelectPopup : MonoBehaviour
{
    public static DeckSelectPopup Instance;

    public GameObject popupPanel;
    public Button[] deckButtons; // 5 buton (0..4)

    private CardData pendingCard;

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < deckButtons.Length; i++)
        {
            int deckIndex = i; // 0-bazlı
            deckButtons[i].onClick.RemoveAllListeners();
            deckButtons[i].onClick.AddListener(() => AddCardToDeck(deckIndex));
        }
    }

    public void ShowDeckChoice(CardData card)
    {
        pendingCard = card;
        if (popupPanel) popupPanel.SetActive(true);
    }

    void AddCardToDeck(int deckIndex)
    {
        var dm = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>(true);
        if (dm == null) { Debug.LogError("DeckManagerObject bulunamadı!"); if (popupPanel) popupPanel.SetActive(false); return; }

        bool ok = dm.AddToDeck(deckIndex, pendingCard); // sprite oto-doldur + limit kontrol içeride
        if (ok)
        {
            Debug.Log($"✅ {pendingCard.cardName} -> {deckIndex + 1}. deste");
        }
        // Başarılı/başarısız fark etmeksizin pop-up kapanıyor (istersen şartlı yap)
        if (popupPanel) popupPanel.SetActive(false);
    }
}
