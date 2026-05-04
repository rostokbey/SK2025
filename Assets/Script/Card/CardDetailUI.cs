using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardDetailUI : MonoBehaviour
{
    public Image characterImage;
    public TMP_Text nameText;
    public TMP_Text hpText;
    public TMP_Text strText;
    public TMP_Text DefText;
    public TMP_Text rarityText;
    public TMP_Text abilityText;
    public TMP_Text passiveText;

    public void ShowDetails(CardData card)
    {
        characterImage.sprite = card.characterSprite;
        nameText.text = card.cardName.Replace("_", " ");
        hpText.text = "HP: " + card.baseHP;
        strText.text = "STR: " + card.baseStr;
        DefText.text = "Def: " + card.baseDef;
        rarityText.text = "Rarity: " + card.rarity;
        abilityText.text = "Ability: " + card.ability;
        passiveText.text = "Passive: " + card.passive;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
