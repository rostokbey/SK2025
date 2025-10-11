using UnityEngine;
using TMPro;
using System.Collections;

public class CardDetailPanel : MonoBehaviour
{
    public static CardDetailPanel Instance;

    [Header("UI Referansları")]
    public GameObject panel;

    public GameObject inventoryPanel; // 💡 Buraya InventoryUI panelini atayacağız (DeckSelectPanel)

    public TMP_Text nameText;
    public TMP_Text dexText;
    public TMP_Text classText;
    public TMP_Text raceText;
    public TMP_Text comboText;
    public TMP_Text storyText;
    public TMP_Text rarityText;
    public TMP_Text passiveText;
    public TMP_Text abilityText;
    public CharacterStoryDatabase storyDatabase;
    public CanvasGroup canvasGroup; // UI > Canvas Group bileşeni
    public float fadeDuration = 0.3f;


    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);
    }

    public void ShowCardDetails(CardData card)
    {
        nameText.text = card.cardName.Replace("_", " ");
        
        classText.text = "Class: " + card.className;
        raceText.text = "Irk: " + card.race;
        comboText.text = "Kombinasyon: " + card.combo;
        storyText.text = "Hikaye: " + storyDatabase.GetStory(card.cardName);
        rarityText.text = "Nadirlik: " + card.rarity;
        passiveText.text = "Pasif: " + card.passive;
        abilityText.text = "Aktif: " + card.ability;

        panel.SetActive(true);
        StartCoroutine(FadeIn());

        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
    }

    IEnumerator FadeIn()
    {
        canvasGroup.alpha = 0;
        float t = 0;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(0, 1, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = 1;
    }


    public void ClosePanel()
    {
        panel.SetActive(false);

        // Envanteri geri getir
        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);
    }
}
