using UnityEngine;
using TMPro;

public class CardUIController : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI damageText;
    public TextMeshProUGUI abilityText;
    public TextMeshProUGUI passiveText;

    public void DisplayCard(CardData card)
    {
        nameText.text = card.cardName;
        hpText.text = "HP: " + card.baseHP;
        damageText.text = "DMG: " + card.baseDamage;
        abilityText.text = "Aktif: " + card.ability;
        passiveText.text = "Pasif: " + card.passive;
    }
}
