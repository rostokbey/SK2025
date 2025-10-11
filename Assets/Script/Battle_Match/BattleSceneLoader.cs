using UnityEngine;

public class BattleSceneLoader : MonoBehaviour
{
    public Transform playerSpawner;
    public Transform enemySpawner;
    public GameObject cardSlotPrefab;

    void Start()
    {
        SpawnPlayerCards();
        SpawnEnemyCards();
    }

    void SpawnPlayerCards()
    {
        foreach (var card in StartBattleManager.Instance.selectedMatchCards)
        {
            GameObject cardGO = Instantiate(cardSlotPrefab, playerSpawner);
            CardUI ui = cardGO.GetComponent<CardUI>();
            if (ui != null)
            {
                ui.SetCardData(card, true); // Oyuncu kartı yüzü açık
            }
        }
        Debug.Log("Oyuncu kartları sahneye basıldı.");
    }

    void SpawnEnemyCards()
    {
        foreach (var card in StartBattleManager.Instance.enemyMatchCards)
        {
            GameObject cardGO = Instantiate(cardSlotPrefab, enemySpawner);
            CardUI ui = cardGO.GetComponent<CardUI>();
            if (ui != null)
            {
                ui.SetCardData(card, false); // Düşman kartı yüzü kapalı
            }
        }
        Debug.Log("Düşman kartları sahneye basıldı.");
    }
}