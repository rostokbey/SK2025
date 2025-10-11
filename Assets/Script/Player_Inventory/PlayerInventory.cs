using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    // -------- Singleton --------
    public static PlayerInventory Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    // -------- Profil / Veri --------
    [Header("Profil")]
    [SerializeField] private string _currentProfileId = "DEFAULT";
    /// <summary>Instance üzerinden profil id (yeni kullanım)</summary>
    public string CurrentProfileId
    {
        get => _currentProfileId;
        set => _currentProfileId = string.IsNullOrWhiteSpace(value) ? "DEFAULT" : value.Trim();
    }
    /// <summary>Eski kodlar için static alias (varsa Instance’ı döndürür).</summary>
    public static string CurrentProfileIdStatic => Instance != null ? Instance.CurrentProfileId : "DEFAULT";
    /// <summary>Eski kodla uyumluluk: PlayerInventory.CurrentProfileId diye static çağrılırsa patlamasın.</summary>
    public static string CurrentProfileId_Compat => CurrentProfileIdStatic;
    // Eski bazı yerlerde doğrudan bu ada referans olabileceği için:
    public static string CurrentProfileIdLegacy => CurrentProfileIdStatic;
    // En çok kullanılan isim:
    public static string CurrentProfileIdAlias => CurrentProfileIdStatic;

    [Header("Kartlar")]
    public List<CardData> myCards = new List<CardData>();

    // (Bazı sahnelerde kullanılan) aktif kart seçimi
    public int activeCardIndex = -1;

    // -------- API --------

    /// <summary>Kart ekler. Aynı ID tekrar okunursa XP verir, kopya eklemez.</summary>
    public void AddCard(CardData card)
    {
        if (card == null) return;

        // Aynı ID varsa duplicate kabul et → XP ödülü ver
        var exist = myCards.Find(x => x.id == card.id);
        if (exist != null)
        {
            int dupXp = CardLevelSystem.Instance?.DuplicateScanXpDefault ?? 100;

            CardLevelSystem.Instance?.AddExperience(exist, dupXp);
            SaveToDisk(); // ilerlemeyi yaz
            Debug.Log($"[Inventory] Duplicate scan → XP +{dupXp}: {exist.cardName} ({exist.id})");
            return;
        }

        // Yeni kart
        myCards.Add(card);
        SaveToDisk();
        Debug.Log($"[Inventory] Eklendi: {card.cardName} ({card.id})");
    }

    /// <summary>Profil kimliği vererek kaydet.</summary>
    public void SaveToDisk(string profileId = null)
    {
        if (!string.IsNullOrEmpty(profileId))
            CurrentProfileId = profileId;

        var data = new InventorySaveData();
        foreach (var c in myCards)
            data.cards.Add(CardDataMapper.ToDTO(c));

        SaveSystem.SaveInventory(CurrentProfileId, data);
    }

    /// <summary>Profil kimliği vererek yükle.</summary>
    public void LoadFromDisk(string profileId)
    {
        CurrentProfileId = profileId;

        myCards.Clear();
        var data = SaveSystem.LoadInventory(CurrentProfileId);
        if (data == null || data.cards == null || data.cards.Count == 0)
        {
            Debug.Log($"[Inventory] Kayıt yok (profil={CurrentProfileId}).");
            return;
        }

        foreach (var dto in data.cards)
        {
            var c = CardDataMapper.FromDTO(dto);

            // Level/XP’ye göre anlık değerleri uygula
            CardLevelSystem.Instance?.ApplyToCard(c);

            // Sprite boş geldiyse en azından listede bir görsel olsun
            if (c.characterSprite == null && QRDataManager.Instance != null)
            {
                var fallback = QRDataManager.Instance.defaultListSprite;
                if (fallback != null) c.characterSprite = fallback;
            }

            myCards.Add(c);
        }

        Debug.Log($"[Inventory] Yüklendi: {myCards.Count} kart (profil={CurrentProfileId}).");
    }

    // -------- Yardımcılar / Eski Kod Uyumluluğu --------

    public CardData GetActiveCard()
    {
        if (myCards == null || myCards.Count == 0) return null;
        if (activeCardIndex < 0 || activeCardIndex >= myCards.Count) return myCards[0];
        return myCards[activeCardIndex];
    }

    public void SetActiveCardIndex(int index)
    {
        if (myCards == null || myCards.Count == 0) { activeCardIndex = -1; return; }
        activeCardIndex = Mathf.Clamp(index, 0, myCards.Count - 1);
    }

    // Eski sahnelerden gelebilecek çağrılar için stub’lar:
    public bool CanUseSkill(CardData c) { return true; }              // İstersen cooldown/enerji kontrolünü burada kur
    public void ResetSkillCooldown(CardData c) { /* cooldown resetle */ }
    public void OnTurnStart() { /* tur başlangıcı mantığı */ }
}