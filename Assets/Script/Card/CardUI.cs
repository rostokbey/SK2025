using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardUI : MonoBehaviour
{
    [Header("UI Referansları")]
    public TMP_Text nameText;
    public TMP_Text hpText;
    public TMP_Text damageText;
    public TMP_Text levelText;
    public TMP_Text xpText;
    public TMP_Text dexText;

    [Tooltip("Kartın ön yüzü (karakter görseli)")]
    public Image characterImage;   // front
    [Tooltip("Kartın arka yüzü (ters kapak görseli)")]
    public Image backImage;        // back (opsiyonel)

    [Header("Butonlar")]
    public Button detailButton;
    public Button selectButton;

    [Header("Battle Durumu")]
    public bool isInBattle = false;

    private CardData cardData;

    // ---- Callback ----
    public System.Action<CardData> onSelect;

    // ---- Kart verisi set etme ----
    public void SetCardData(CardData data, bool showButtons = true)
    {
        cardData = data;

        if (nameText) nameText.text = data.cardName.Replace("_", " ");
        if (hpText) hpText.text = "HP: " + data.baseHP;
        if (damageText) damageText.text = "STR: " + data.baseDamage;
        if (levelText) levelText.text = "Lv: " + data.level;
        if (xpText) xpText.text = "XP: " + data.xp + "/100";
        if (dexText) dexText.text = "DEX: " + data.baseDex;

        if (characterImage) characterImage.sprite = data.characterSprite;

        // ---- Butonları ayarlama ----
        if (detailButton)
        {
            detailButton.gameObject.SetActive(showButtons && !isInBattle);
            detailButton.onClick.RemoveAllListeners();
            detailButton.onClick.AddListener(OnDetailButtonClicked);
        }

        if (selectButton)
        {
            selectButton.gameObject.SetActive(showButtons);
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnSelectClicked);
        }
    }

    // ---- Ön/arka yüz ----
    public void SetFaceUp(bool faceUp)
    {
        if (characterImage) characterImage.gameObject.SetActive(faceUp);
        if (backImage) backImage.gameObject.SetActive(!faceUp);
    }

    // ---- Tıklamalar ----

    public void OnSelectClicked()
    {
        if (isInBattle)
        {
            // Savaşta: HandUIManager veya benzeri bir sistem dinler
            onSelect?.Invoke(cardData);
        }
        else
        {
            // Envanterde: Deck seçimi popup
            DeckSelectPopup.Instance?.ShowDeckChoice(this.cardData);
        }
    }
    public CardData GetCardData()
    {
        return cardData;
    }
    public void OnDetailButtonClicked()
    {
        if (cardData == null) return;

        // CardDetailPanel’i aç
        if (CardDetailPanel.Instance != null)
        {
            CardDetailPanel.Instance.ShowCardDetails(cardData);
        }
        else
        {
            Debug.LogWarning("CardDetailPanel sahnede bulunamadı!");
        }
    }

    // Kart tıklanınca savaşta yerleştirme modu
    public System.Action<CardUI> onCardClicked;

    public void OnCardClicked()
    {
        if (!isInBattle) return;

        onCardClicked?.Invoke(this);
    }

}
