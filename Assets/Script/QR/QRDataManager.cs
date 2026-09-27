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
    public TextMeshProUGUI nameText, hpText, strText, defText, abilityText, passiveText;
    public Sprite defaultListSprite;

    public static Action<CardData> OnCardReady;

    void Awake() => Instance = this;

    public void ParseQRData(string jwtText)

        
    {
        Debug.Log("[QRDataManager] ParseQRData çalıştı. Gelen veri: " + jwtText);

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

        Debug.Log("QR JTI: " + p.jti);

        CardData dbCard = CardDatabase.AllCards.FirstOrDefault(c => c.id == p.id);
        CardData card;

        if (dbCard != null)
        {
            card = dbCard.Clone();
            Debug.Log($"[QR] Savaş kartı bulundu: {card.cardName} ({card.id})");
        }
        else
        {
            if (nameText) nameText.text = p.name?.Replace("_", " ") ?? "";
            if (hpText) hpText.text = "HP: " + p.hp;
            if (strText) strText.text = "STR: " + p.str;
            if (defText) defText.text = "DEF: " + p.def;
            if (abilityText) abilityText.text = "Ability: " + p.ability;
            if (passiveText) passiveText.text = "Passive: " + p.passive;

            Sprite listSprite = null;

            var spriteManager =
                DeckManagerObject.Instance ??
                FindObjectOfType<DeckManagerObject>();

            if (spriteManager != null)
                listSprite = spriteManager.GetSpriteByName(p.name);

            if (listSprite == null && !string.IsNullOrEmpty(p.name))
            {
                string spriteName = p.name.Trim().Replace(" ", "_");
                listSprite = Resources.Load<Sprite>(
                    "Characters/" + spriteName);
            }

            if (listSprite == null)
            {
                Debug.LogError(
                    "[QR] Karakter görseli bulunamadı. Aranan isim: " +
                    p.name);
                return;
            }

            if (characterImage)
                characterImage.sprite = listSprite;





            card = new CardData(
            p.id, p.name,
            p.hp, p.str, p.def, // QR'da base olarak alıyoruz
            p.rarity, p.ability, p.passive,
            1,
            0,
            3,
            listSprite,
            null,
             p.prefab
);

            Debug.Log($"[QR] Karakter kartı oluşturuldu: {card.cardName} ({card.id})");
        }

        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv != null)
        {
            inv.AddCard(card);
        }
        else
        {
            Debug.LogWarning("[QR] PlayerInventory bulunamadı.");
        }

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