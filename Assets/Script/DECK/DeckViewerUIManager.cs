using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckViewerUIManager : MonoBehaviour
{
    [Header("UI")]
    public GameObject deckViewPanel;     // Gösterilecek ana panel
    public Transform decksParent;        // ScrollView/Viewport/DecksParent
    public GameObject deckPanelPrefab;   // İçinde: DeckTitle + Content (Grid+Fitter)
    public GameObject cardImagePrefab;   // (opsiyonel) Sadece Image içeren prefab

    private DeckManagerObject deckManager;

    private void Awake()
    {
        deckManager = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>(true);
    }

    public void ShowAllDecks()
    {
        Debug.Log($"[CHECK] characterSprites={deckManager.characterSprites.Count}");

        if (deckViewPanel != null && !deckViewPanel.activeSelf)
            deckViewPanel.SetActive(true);

        if (deckManager == null) { Debug.LogError("❌ DeckManagerObject yok."); return; }
        if (decksParent == null) { Debug.LogError("❌ decksParent atanmadı."); return; }
        if (deckPanelPrefab == null) { Debug.LogError("❌ deckPanelPrefab atanmadı."); return; }

        // Eski panelleri temizle
        for (int i = decksParent.childCount - 1; i >= 0; i--)
            Destroy(decksParent.GetChild(i).gameObject);

        // 0..4 desteleri sırayla bas
        for (int deckIdx = 0; deckIdx < 5; deckIdx++)
        {
            var deck = deckManager.GetDeckByIndex(deckIdx);
            if (deck == null) continue;

            // Panel
            var panel = Instantiate(deckPanelPrefab, decksParent, false);
            panel.name = $"Deck_{deckIdx + 1}";

            var title = panel.transform.Find("DeckTitle")?.GetComponent<TMP_Text>();
            if (title) title.text = $"Deste {deckIdx + 1} ({deck.Count}/{deckManager.deckMaxSize})";

            var content = panel.transform.Find("Content");
            if (content == null) { Debug.LogError("DeckPanelPrefab içinde 'Content' yok."); continue; }

            // Sadece kart görselleri
            foreach (var card in deck)
            {
                if (card == null) continue;
                Debug.Log($"[CARD] {card.cardName} sprite={(card.characterSprite ? card.characterSprite.name : "NULL")}");

                if (card.characterSprite == null && !string.IsNullOrEmpty(card.cardName))
                {
                    card.characterSprite = deckManager.GetSpriteByName(card.cardName);
                    if (card.characterSprite == null) { Debug.LogWarning($"Sprite bulunamadı: {card.cardName}"); continue; }
                }

                // Sprite hala boşsa atla
                if (card.characterSprite == null) continue;

                GameObject go = (cardImagePrefab != null)
                    ? Instantiate(cardImagePrefab, content, false)
                    : new GameObject($"IMG_{card.cardName}", typeof(RectTransform), typeof(Image));

                var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
                img.sprite = card.characterSprite;
                img.preserveAspect = true;
                img.color = Color.white;

                var rt = go.GetComponent<RectTransform>();
                if (rt) rt.sizeDelta = new Vector2(260, 360); // Grid hücrene göre
            }

            // 🔧 İçerik dolduktan sonra: layout tazele + kaç çocuk var logla
            var cRect = content as RectTransform;
            if (cRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(cRect);
                Debug.Log($"[UI] {panel.name} content childCount={cRect.childCount}");
            }
        }

        // Parent tazele
        var parentRect = decksParent as RectTransform;
        if (parentRect) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
    }

    public void HideDecksPanel()
    {
        if (deckViewPanel != null) deckViewPanel.SetActive(false);
    }
}
