using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CardPanelUI : MonoBehaviour
{
    public Image characterImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;

    public void SetCardInfo(CardData card)
    {
        nameText.text = card.cardName;
        levelText.text = "Seviye: " + card.level;
        xpText.text = "XP: " + card.xp + "/100";
        characterImage.sprite = card.characterSprite;
    }
}
