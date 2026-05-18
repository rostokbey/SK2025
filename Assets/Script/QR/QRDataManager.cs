using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;
using System.Collections.Generic;

public class QRDataManager : MonoBehaviour
{
    public static QRDataManager Instance;

    [Header("Preview UI (opsiyonel)")]
    public Image characterImage;
    public TextMeshProUGUI nameText, hpText, strText, defText, abilityText, passiveText;
    public Sprite defaultListSprite;

    public static Action<CardData> OnCardReady;

    private const string UsedQrKey = "USED_QR_JTIS";

    [Serializable]
    private class UsedQrSaveData
    {
        public List<string> usedJtis = new List<string>();
    }

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

        if (string.IsNullOrEmpty(p.jti))
        {
            Debug.LogError("[QR] Bu QR içinde JTI yok. Tek kullanımlık sistem çalışamaz.");
            return;
        }

        Debug.Log("QR JTI: " + p.jti);
        Debug.Log("QR Card ID: " + p.id);

        var usedJtis = LoadUsedJtis();

        // 1) Aynı QR daha önce okutulduysa hiçbir işlem yapma
        if (usedJtis.Contains(p.jti))
        {
            Debug.LogWarning("[QR] Bu QR daha önce kullanılmış. İşlem yapılmadı.");
            return;
        }

        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null)
        {
            Debug.LogWarning("[QR] PlayerInventory bulunamadı.");
            return;
        }

        // 2) QR ilk kez okutuluyor ama aynı karakter envanterde varsa XP ver
        var existingCharacter = inv.myCards.FirstOrDefault(c => c != null && c.id == p.id);

        if (existingCharacter != null)
        {
            Debug.Log($"[QR] Aynı karakter zaten var. Yeni kart eklenmedi, XP veriliyor: {existingCharacter.cardName}");

            if (CardLevelSystem.Instance != null)
            {
                CardLevelSystem.Instance.AdDefperience(
                    existingCharacter,
                    CardLevelSystem.Instance.DuplicateScanXpDefault
                );
            }

            usedJtis.Add(p.jti);
            SaveUsedJtis(usedJtis);

            inv.SaveToDisk();

            OnCardReady?.Invoke(existingCharacter);
            return;
        }

        // 3) QR ilk kez okutuluyor ve karakter de yoksa yeni karakter ekle
        CardData dbCard = CardDatabase.AllCards.FirstOrDefault(c => c.id == p.id);
        CardData card;

        if (dbCard != null)
        {
            card = dbCard.Clone();
            card.jti = p.jti;

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

            if (!string.IsNullOrEmpty(p.name))
                listSprite = Resources.Load<Sprite>("Characters/" + p.name);

            if (!listSprite)
                listSprite = defaultListSprite;

            if (characterImage)
                characterImage.sprite = listSprite;

            card = new CardData(
                p.id,
                p.name,
                p.hp,
                p.str,
                p.def,
                p.rarity,
                p.ability,
                p.passive,
                1,
                0,
                3,
                listSprite,
                null,
                p.prefab
            );

            card.jti = p.jti;

            Debug.Log($"[QR] Yeni karakter kartı oluşturuldu: {card.cardName} ({card.id})");
        }

        inv.AddCard(card);

        usedJtis.Add(p.jti);
        SaveUsedJtis(usedJtis);

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

    private HashSet<string> LoadUsedJtis()
    {
        string json = PlayerPrefs.GetString(UsedQrKey, "");

        if (string.IsNullOrEmpty(json))
            return new HashSet<string>();

        UsedQrSaveData data = JsonUtility.FromJson<UsedQrSaveData>(json);

        if (data == null || data.usedJtis == null)
            return new HashSet<string>();

        return new HashSet<string>(data.usedJtis);
    }

    private void SaveUsedJtis(HashSet<string> usedJtis)
    {
        UsedQrSaveData data = new UsedQrSaveData
        {
            usedJtis = usedJtis.ToList()
        };

        string json = JsonUtility.ToJson(data, true);

        PlayerPrefs.SetString(UsedQrKey, json);
        PlayerPrefs.Save();

        Debug.Log("[QR] Kullanılmış QR listesi kaydedildi. Toplam: " + usedJtis.Count);
    }
}