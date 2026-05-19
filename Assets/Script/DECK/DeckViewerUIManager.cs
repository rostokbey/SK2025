using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckViewerUIManager : MonoBehaviour
{
    [Header("UI")]
    public GameObject deckViewPanel;
    public Transform decksParent;
    public GameObject deckPanelPrefab;
    public GameObject cardImagePrefab;

    private DeckManagerObject deckManager;

    private int selectedDeckIndex = 0; // 0 = Deste 1

    private void Awake()
    {
        deckManager = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>(true);
    }

    public void SetSelectedDeck(int index)
    {
        selectedDeckIndex = Mathf.Clamp(index, 0, 4);
        Debug.Log("Seçilen deste: " + (selectedDeckIndex + 1));
    }

    public void ShowSelectedDeck()
    {
        if (deckManager == null)
            deckManager = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>(true);

        if (deckViewPanel != null && !deckViewPanel.activeSelf)
            deckViewPanel.SetActive(true);

        if (deckManager == null)
        {
            Debug.LogError("❌ DeckManagerObject yok.");
            return;
        }

        if (decksParent == null)
        {
            Debug.LogError("❌ decksParent atanmadı.");
            return;
        }

        if (deckPanelPrefab == null)
        {
            Debug.LogError("❌ deckPanelPrefab atanmadı.");
            return;
        }

        for (int i = decksParent.childCount - 1; i >= 0; i--)
        {
            Destroy(decksParent.GetChild(i).gameObject);
        }

        var deck = deckManager.GetDeckByIndex(selectedDeckIndex);

        if (deck == null)
        {
            Debug.LogWarning("Seçilen deste boş/null: " + (selectedDeckIndex + 1));
            return;
        }

        GameObject panel = Instantiate(deckPanelPrefab, decksParent, false);
        panel.name = "Deck_" + (selectedDeckIndex + 1);

        TMP_Text title = panel.transform.Find("DeckTitle")?.GetComponent<TMP_Text>();
        if (title != null)
        {
            title.text = "Deste " + (selectedDeckIndex + 1) + " (" + deck.Count + "/" + deckManager.deckMaxSize + ")";
        }

        Transform content = panel.transform.Find("Content");

        if (content == null)
        {
            Debug.LogError("DeckPanelPrefab içinde 'Content' yok.");
            return;
        }

        foreach (var card in deck)
        {
            if (card == null)
                continue;

            if (card.characterSprite == null && !string.IsNullOrEmpty(card.cardName))
            {
                card.characterSprite = deckManager.GetSpriteByName(card.cardName);

                if (card.characterSprite == null)
                {
                    Debug.LogWarning("Sprite bulunamadı: " + card.cardName);
                    continue;
                }
            }

            if (card.characterSprite == null)
                continue;

            GameObject go;

            if (cardImagePrefab != null)
                go = Instantiate(cardImagePrefab, content, false);
            else
                go = new GameObject("IMG_" + card.cardName, typeof(RectTransform), typeof(Image));

            Image img = go.GetComponent<Image>();
            if (img == null)
                img = go.AddComponent<Image>();

            img.sprite = card.characterSprite;
            img.preserveAspect = true;
            img.color = Color.white;

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = new Vector2(260, 360);
        }

        RectTransform contentRect = content as RectTransform;
        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        RectTransform parentRect = decksParent as RectTransform;
        if (parentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
    }

    public void HideDecksPanel()
    {
        if (deckViewPanel != null)
            deckViewPanel.SetActive(false);
    }
}