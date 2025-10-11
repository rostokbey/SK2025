using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;

public class QRDataManager : MonoBehaviour
{
    public static QRDataManager Instance;

    [Header("Preview UI (opsiyonel)")]
    public Image characterImage;
    public TextMeshProUGUI nameText, hpText, strText, abilityText, passiveText;
    public Sprite defaultListSprite;

    public static Action<CardData> OnCardReady;

    void Awake() => Instance = this;

    public void ParseQRData(string jwtText)
    {
        if (string.IsNullOrWhiteSpace(jwtText))
        {
            Debug.LogWarning("[QR] Boş QR metni.");
            return;
        }

        if (!JwtCardVerifier.TryParse(jwtText, out var p, out string err))
        {
            Debug.LogError("[QR] JWT çözümleme hatası: " + err);
            return;
        }

        // Önce CardDatabase içinde savaş kartı mı kontrol et
        CardData dbCard = CardDatabase.AllCards.FirstOrDefault(c => c.id == p.id);
        CardData card;

        if (dbCard != null)
        {
            // Savaş kartı → hazır tanımdan kopya al
            card = dbCard.Clone();
            Debug.Log($"[QR] Savaş kartı bulundu: {card.cardName} ({card.id})");
        }
        else
        {
            // Normal karakter kartı
            if (nameText) nameText.text = p.name?.Replace("_", " ") ?? "";
            if (hpText) hpText.text = "HP: " + p.hp;
            if (strText) strText.text = "STR: " + p.str;
            if (abilityText) abilityText.text = "Ability: " + p.ability;
            if (passiveText) passiveText.text = "Passive: " + p.passive;

            Sprite listSprite = null;
            if (!string.IsNullOrEmpty(p.name))
                listSprite = Resources.Load<Sprite>("Characters/" + p.name);
            if (!listSprite) listSprite = defaultListSprite;
            if (characterImage) characterImage.sprite = listSprite;

            int baseDex = 0, dex = 0;

            card = new CardData(
                p.id, p.name,
                p.hp, p.str,
                baseDex, dex,
                p.rarity, p.ability, p.passive,
                1,           // level
                0,           // xp
                3,           // skillCooldownMax (starter)
                listSprite,
                null,
                p.prefab
            );
            Debug.Log($"[QR] Karakter kartı oluşturuldu: {card.cardName} ({card.id})");
        }

        // Envantere ekle
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv != null)
        {
            inv.AddCard(card);
        }
        else
        {
            Debug.LogWarning("[QR] PlayerInventory bulunamadı.");
        }

        // 🔹 DeckManagerObject’e de ekle (yeni eklenen kısım)
        var deckManager = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>();
        if (deckManager != null)
        {
            if (!deckManager.fullDeck.Exists(c => c.id == card.id))
            {
                deckManager.fullDeck.Add(card);
                Debug.Log($"[QR] DeckManagerObject.fullDeck içine eklendi: {card.cardName} ({card.id})");
            }
        }
        else
        {
            Debug.LogWarning("[QR] DeckManagerObject bulunamadı.");
        }

        OnCardReady?.Invoke(card);
    }
}
