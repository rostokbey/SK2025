using UnityEngine;
using System.Collections.Generic;

public class CardCollectionManager : MonoBehaviour
{
    public GameObject cardPrefab;          // CardUI_Inventory.prefab
    public Transform scrollViewContent;    // Scroll View > Viewport > Content

    void Start()
    {
        // 0) UI'ı temizle — sahne yeniden açıldıysa birikmesin
        for (int i = scrollViewContent.childCount - 1; i >= 0; i--)
            Destroy(scrollViewContent.GetChild(i).gameObject);

        // 1) Envanteri al
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null)
        {
            Debug.LogError("PlayerInventory bulunamadı!");
            return;
        }

        // 2) Aynı id'yi iki kere göstermeyelim
        var shown = new HashSet<string>();

        foreach (var card in inv.myCards)
            AddCardUI(card, shown);
    }

    private void AddCardUI(CardData card, HashSet<string> shown = null)
    {
        if (card == null || string.IsNullOrEmpty(card.id)) return;
        if (shown != null && !shown.Add(card.id)) return; // zaten eklendi

        var go = Instantiate(cardPrefab, scrollViewContent);
        go.transform.localScale = Vector3.one;

        var controller = go.GetComponent<CardUIController>();
        controller?.DisplayCard(card);
    }

    // — İsteğe bağlı: Component başlığında sağ tık > Temizle (UI + Envanter)
    [ContextMenu("Temizle (UI + Envanter)")]
    public void ClearAll()
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv != null)
        {
            inv.myCards.Clear();
            inv.SaveToDisk();
        }

        for (int i = scrollViewContent.childCount - 1; i >= 0; i--)
            DestroyImmediate(scrollViewContent.GetChild(i).gameObject);

        Debug.Log("[Collection] Envanter ve UI temizlendi.");
    }
}