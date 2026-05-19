using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    [Header("Profil")]
    [SerializeField] private string _currentProfileId = "DEFAULT";

    public string CurrentProfileId
    {
        get => _currentProfileId;
        set => _currentProfileId = string.IsNullOrWhiteSpace(value) ? "DEFAULT" : value.Trim();
    }

    public static string CurrentProfileIdStatic => Instance != null ? Instance.CurrentProfileId : "DEFAULT";
    public static string CurrentProfileId_Compat => CurrentProfileIdStatic;
    public static string CurrentProfileIdLegacy => CurrentProfileIdStatic;
    public static string CurrentProfileIdAlias => CurrentProfileIdStatic;

    [Header("Kartlar")]
    public List<CardData> myCards = new List<CardData>();

    public int activeCardIndex = -1;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void AddCard(CardData card)
    {
        if (card == null)
            return;

        // Aynı karakter varsa: yeni kart ekleme, XP ver
        var sameCharacter = myCards.Find(x =>
            x != null &&
            x.cardName == card.cardName
        );

        if (sameCharacter != null)
        {
            int dupXp = CardLevelSystem.Instance != null
                ? CardLevelSystem.Instance.DuplicateScanXpDefault
                : 100;

            CardLevelSystem.Instance?.AdDefperience(sameCharacter, dupXp);

            SaveToDisk();

            Debug.Log("[Inventory] Aynı karakter tekrar okutuldu -> XP +" + dupXp + " : " + sameCharacter.cardName);
            return;
        }

        // Yeni karakterse ekle
        myCards.Add(card);

        SaveToDisk();

        Debug.Log("[Inventory] Yeni karakter eklendi: " + card.cardName);
    }

    public void SaveToDisk(string profileId = null)
    {
        if (!string.IsNullOrEmpty(profileId))
            CurrentProfileId = profileId;

        var data = new InventorySaveData();

        foreach (var c in myCards)
        {
            if (c != null)
                data.cards.Add(CardDataMapper.ToDTO(c));
        }

        SaveSystem.SaveInventory(CurrentProfileId, data);
    }

    public void LoadFromDisk(string profileId)
    {
        CurrentProfileId = profileId;

        myCards.Clear();

        var data = SaveSystem.LoadInventory(CurrentProfileId);

        if (data == null || data.cards == null || data.cards.Count == 0)
        {
            Debug.Log("[Inventory] Kayıt yok (profil=" + CurrentProfileId + ").");
            return;
        }

        foreach (var dto in data.cards)
        {
            var c = CardDataMapper.FromDTO(dto);

            CardLevelSystem.Instance?.ApplyToCard(c);

            if (c.characterSprite == null && QRDataManager.Instance != null)
            {
                var fallback = QRDataManager.Instance.defaultListSprite;
                if (fallback != null)
                    c.characterSprite = fallback;
            }

            myCards.Add(c);
        }

        Debug.Log("[Inventory] Yüklendi: " + myCards.Count + " kart (profil=" + CurrentProfileId + ").");
    }

    public CardData GetActiveCard()
    {
        if (myCards == null || myCards.Count == 0)
            return null;

        if (activeCardIndex < 0 || activeCardIndex >= myCards.Count)
            return myCards[0];

        return myCards[activeCardIndex];
    }

    public void SetActiveCardIndex(int index)
    {
        if (myCards == null || myCards.Count == 0)
        {
            activeCardIndex = -1;
            return;
        }

        activeCardIndex = Mathf.Clamp(index, 0, myCards.Count - 1);
    }

    public bool CanUseSkill(CardData c)
    {
        return true;
    }

    public void ResetSkillCooldown(CardData c)
    {
    }

    public void OnTurnStart()
    {
    }
}