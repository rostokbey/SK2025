using UnityEngine;

public class InventoryPanel : MonoBehaviour
{
    public GameObject cardUIPrefab; // 💡 CardUI Prefab
    public Transform contentArea;   // 🔽 GridLayout içeren Content objesi

    private void OnEnable()
    {
        RefreshInventory();
    }

    public void RefreshInventory()
    {
        // Önce içerikleri temizle
        foreach (Transform child in contentArea)
        {
            Destroy(child.gameObject);
        }

        // Tüm kartları UI'da göster
        foreach (CardData card in PlayerInventory.Instance.myCards)
        {
            GameObject cardUIObj = Instantiate(cardUIPrefab, contentArea);
            CardUI cardUI = cardUIObj.GetComponent<CardUI>();
            cardUI.SetCardData(card, true);
        }
    }
}
