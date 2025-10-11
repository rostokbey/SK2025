using System.Collections.Generic;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private Transform contentArea;     // Kartların ekleneceği Content
    [SerializeField] private GameObject cardUIPrefab;   // Üzerinde CardUI_Inventory olan prefab

    private void OnEnable()
    {
        RefreshInventory();
    }

    public void RefreshInventory()
    {
        if (contentArea == null || cardUIPrefab == null || PlayerInventory.Instance == null)
            return;

        // Eski itemları temizle
        foreach (Transform child in contentArea)
            Destroy(child.gameObject);

        // Mevcut kartları listele
        List<CardData> cards = PlayerInventory.Instance.myCards;
        if (cards == null) return;

        foreach (var card in cards)
        {
            var go = Instantiate(cardUIPrefab, contentArea);
            var ui = go.GetComponent<CardUI>();   // Envanter için kullandığımız script
            if (ui != null)
                ui.SetCardData(card);
        }
    }
}
