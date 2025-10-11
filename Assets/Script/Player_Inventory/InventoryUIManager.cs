using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUIManager : MonoBehaviour
{
    public GameObject cardPrefab; // CardUI prefab
    public Transform contentParent; // ScrollView > Content nesnesi

    public void DisplayInventory(List<CardData> cards)
    {
        // Önce eski kartlarý temizle
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        // Her kart için prefab oluþtur
        foreach (CardData card in cards)
        {
            GameObject cardGO = Instantiate(cardPrefab, contentParent);
            cardGO.transform.Find("Image").GetComponent<Image>().sprite = card.characterSprite;
            cardGO.transform.Find("NameText").GetComponent<TMP_Text>().text = card.cardName.Replace("_", " ");
            cardGO.transform.Find("HPText").GetComponent<TMP_Text>().text = "HP: " + card.baseHP;
            cardGO.transform.Find("STRText").GetComponent<TMP_Text>().text = "STR: " + card.baseDamage;
        }
    }

    public void OnOpenInventory()
    {
        List<CardData> cards = FindObjectOfType<PlayerInventory>().myCards;
        DisplayInventory(cards);
    }
}

