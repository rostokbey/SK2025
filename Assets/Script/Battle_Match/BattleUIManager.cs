using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class BattleUIManager : MonoBehaviour
{
    public static BattleUIManager Instance; // Singleton

    [Header("Oyuncu Alanı")]
    public Image playerImage;
    public TMP_Text playerNameText;
    public TMP_Text playerHPText;
    public TMP_Text playerSTRText;
    public TMP_Text playerLevelText;
    public TMP_Text playerXPText;

    [Header("Düşman Alanı")]
    public Image enemyImage;
    public TMP_Text enemyNameText;
    public TMP_Text enemyHPText;
    public TMP_Text enemySTRText;
    public TMP_Text enemyLevelText;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void SetupBattleUI(CardData playerCard, CardData enemyCard)
    {
        // Oyuncu kartı bilgileri
        if (playerCard != null)
        {
            playerImage.sprite = playerCard.characterSprite;
            playerNameText.text = playerCard.cardName.Replace("_", " ");
            playerHPText.text = "HP: " + playerCard.baseHP; playerSTRText.text = "STR: " + playerCard.baseDamage;
            playerLevelText.text = "Lv: " + playerCard.level;
            playerXPText.text = "XP: " + playerCard.xp + "/100";
        }

        // Düşman kartı bilgileri
        if (enemyCard != null)
        {
            enemyImage.sprite = enemyCard.characterSprite; // Null olabilir
            enemyNameText.text = enemyCard.cardName;
            enemyHPText.text = "HP: " + enemyCard.baseHP;
            enemySTRText.text = "STR: " + enemyCard.baseDamage;
            enemyLevelText.text = "Lv: " + enemyCard.level;
        }
    }
}