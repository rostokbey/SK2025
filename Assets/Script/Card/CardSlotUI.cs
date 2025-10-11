using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardSlotUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;
    public Image characterImage;
    public Button button;

    public void SetCardInfo(CardData card)
    {
        Debug.Log("🎴 SetCardInfo çağrıldı: " + card.cardName);
        Debug.Log("📸 Sprite var mı?: " + (card.characterSprite != null));

        nameText.text = card.cardName;
        levelText.text = "Seviye: " + card.level;
        xpText.text = "XP: " + card.xp + "/100";
        characterImage.sprite = card.characterSprite;
    }
}
