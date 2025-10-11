using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DeckSelectionUIManager : MonoBehaviour
{
    public RectTransform contentParent; // ScrollView -> Content
    public GameObject cardUIPrefab;     // CardUI prefab
    public DeckManagerObject deckManager;

    [Header("UI Referansları")]
    public GameObject bottomNav;   // BottomNav paneli
    public GameObject mainMenuBtn; // Ana Menü butonu (Canvas içinde)

    void OnEnable()
    {
        // Panel açılınca: BottomNav kapansın, MainMenu açılsın
        //if (bottomNav) bottomNav.SetActive(false);
        //if (mainMenuBtn) mainMenuBtn.SetActive(true);

        // ModalGuard varsa aktif et
        // ModalGuard.Open(this.gameObject);
    }

    void OnDisable()
    {
        // Panel kapanınca: BottomNav tekrar açılsın, MainMenu kapansın
        //if (bottomNav) bottomNav.SetActive(true);
        //if (mainMenuBtn) mainMenuBtn.SetActive(false);

        //ModalGuard.Close(this.gameObject);
    }

    void Start()
    {
        if (deckManager == null)
        {
            deckManager = FindObjectOfType<DeckManagerObject>();
            if (deckManager == null)
            {
                Debug.LogError("❌ DeckManagerObject bulunamadı!");
                return;
            }
        }

        DisplayDeckOptions();
    }

    public void DisplayDeckOptions()
    {
        if (deckManager == null || deckManager.fullDeck.Count == 0)
        {
            Debug.LogWarning("❌ DeckManager yok veya kart eklenmemiş.");
            return;
        }

        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        foreach (CardData card in deckManager.fullDeck)
        {
            GameObject cardGO = Instantiate(cardUIPrefab, contentParent);
            CardUI cardUI = cardGO.GetComponent<CardUI>();
            if (cardUI != null)
            {
                cardUI.SetCardData(card, false);
            }
        }
    }
}