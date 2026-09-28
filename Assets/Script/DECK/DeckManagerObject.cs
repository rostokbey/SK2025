using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.IO;

public class DeckManagerObject : MonoBehaviour
{
    public static DeckManagerObject Instance;

    [Header("Deste Ayarları")]
    public int deckMinSize = 5;
    public int deckMaxSize = 25;
    public int battlePlayableLimit = 12;

    [Header("Veri Kaynağı")]
    public CardCollection masterCardCollection;

    [Header("JSON Veri Kaynağı")]
    public string jsonResourcePath = "CardDatabase/cards";

    public List<CardData> fullDeck = new List<CardData>();
    public List<CardData> currentMatchDeck = new List<CardData>();
    public List<CardData> matchDeck = new List<CardData>();

    public List<CardData> deck1 = new List<CardData>();
    public List<CardData> deck2 = new List<CardData>();
    public List<CardData> deck3 = new List<CardData>();
    public List<CardData> deck4 = new List<CardData>();
    public List<CardData> deck5 = new List<CardData>();

    [System.Serializable]
    public class CharacterSprite
    {
        public string characterName;
        public Sprite sprite;
    }

    [System.Serializable]
    public class CardJsonItem
    {
        public string id;
        public string cardId;
        public string cardName;

        public int baseHP;
        public int baseStr;
        public int baseDef;

        public string rarity;
        public string ability;
        public string passive;

        public int level;
        public int xp;
        public int skillCooldownMax;

        public string className;
        public string race;
        public string combo;

        public string prefab;

        public int damage;
        public int heal;
        public int duration;

        public float critChance;
        public float critDamage;
        public float reflect;

        public bool stun;
        public bool banish;
        public int summonCount;
        public bool combine;
        public bool disableAttack;

        public string type;
    }

    [System.Serializable]
    public class CardJsonDatabase
    {
        public List<CardJsonItem> cards;
    }

    public List<CharacterSprite> characterSprites = new List<CharacterSprite>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadFullDeckFromCollection();
        LoadDecks();
    }

    private string GetCardSaveId(CardData card)
    {
        if (card == null)
            return "";

        // Önce gerçek database id
        if (!string.IsNullOrEmpty(card.cardId))
            return card.cardId;

        // sonra id
        if (!string.IsNullOrEmpty(card.id))
            return card.id;

        // fallback
        return card.cardName;
    }

    // NOT (2026 güvenlik incelemesi): Bu metotlar daha önce ortak/profilsiz bir
    // "player_decks.json" dosyasına imzasız düz JSON olarak yazıyordu; hem
    // SaveSystem'deki profil bazlı deste kaydını hiç kullanmıyordu (ölü kod)
    // hem de tüm profiller aynı dosyayı paylaştığı için profil izolasyonu
    // bozuktu. Artık PlayerInventory ile aynı profil kimliğini kullanan,
    // bütünlük imzalı SaveSystem üzerinden kaydediyor/yüklüyoruz.
    public void SaveDecks()
    {
        string profileId = PlayerInventory.CurrentProfileIdStatic;

        DeckSaveData data = new DeckSaveData();

        data.deck1_CardIds = deck1.Select(GetCardSaveId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        data.deck2_CardIds = deck2.Select(GetCardSaveId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        data.deck3_CardIds = deck3.Select(GetCardSaveId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        data.deck4_CardIds = deck4.Select(GetCardSaveId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        data.deck5_CardIds = deck5.Select(GetCardSaveId).Where(id => !string.IsNullOrEmpty(id)).ToList();

        SaveSystem.SaveDecks(profileId, data);

        Debug.Log("💾 Desteler kaydedildi (profil: " + profileId + ")");
    }

    public void LoadDecks()
    {
        string profileId = PlayerInventory.CurrentProfileIdStatic;

        DeckSaveData data = SaveSystem.LoadDecks(profileId);

        if (data == null)
        {
            Debug.LogWarning("⚠ Deste kaydı okunamadı.");
            return;
        }

        deck1 = ConvertIdsToDeck(data.deck1_CardIds);
        deck2 = ConvertIdsToDeck(data.deck2_CardIds);
        deck3 = ConvertIdsToDeck(data.deck3_CardIds);
        deck4 = ConvertIdsToDeck(data.deck4_CardIds);
        deck5 = ConvertIdsToDeck(data.deck5_CardIds);

        Debug.Log("📂 Desteler yüklendi.");
    }

    private List<CardData> ConvertIdsToDeck(List<string> ids)
    {
        List<CardData> result = new List<CardData>();

        if (ids == null) return result;

        foreach (string id in ids)
        {
            CardData card = GetCardById(id);

            if (card != null)
            {
                if (card.characterSprite == null)
                    card.characterSprite = GetSpriteByName(card.cardName);

                result.Add(card);
            }
            else
            {
                Debug.LogWarning("⚠ Kayıttaki kart bulunamadı: " + id);
            }
        }

        return result;
    }

    private void LoadFullDeckFromCollection()
    {
        fullDeck.Clear();

        bool loadedFromJson = LoadFullDeckFromJson();

        if (loadedFromJson)
            return;

        LoadFullDeckFromMasterCollection();
    }

    private bool LoadFullDeckFromJson()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(jsonResourcePath);

        if (jsonFile == null)
        {
            Debug.LogWarning("⚠ JSON kart verisi bulunamadı: Resources/" + jsonResourcePath);
            return false;
        }

        CardJsonDatabase db = JsonUtility.FromJson<CardJsonDatabase>(jsonFile.text);

        if (db == null || db.cards == null || db.cards.Count == 0)
        {
            Debug.LogError("❌ JSON okundu ama kart listesi boş veya hatalı.");
            return false;
        }

        foreach (var item in db.cards)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
                continue;

            CardData card = new CardData(
                item.id,
                string.IsNullOrEmpty(item.cardName) ? item.id : item.cardName,
                item.baseHP,
                item.baseStr,
                item.baseDef,
                string.IsNullOrEmpty(item.rarity) ? "Common" : item.rarity,
                item.ability,
                item.passive,
                item.level <= 0 ? 1 : item.level,
                item.xp,
                item.skillCooldownMax,
                null,
                null,
                item.prefab
            );

            card.cardId = string.IsNullOrEmpty(item.cardId) ? item.id : item.cardId;

            card.className = item.className;
            card.race = item.race;
            card.combo = item.combo;

            card.damage = item.damage;
            card.heal = item.heal;
            card.duration = item.duration;

            card.critChance = item.critChance;
            card.critDamage = item.critDamage;
            card.reflect = item.reflect;

            card.stun = item.stun;
            card.banish = item.banish;
            card.summonCount = item.summonCount;
            card.combine = item.combine;
            card.disableAttack = item.disableAttack;

            card.type = ParseCardType(item.type);

            card.ResetStats();

            fullDeck.Add(card);
        }

        Debug.Log("✅ JSON Master Deck Yüklendi: " + fullDeck.Count + " kart bulundu.");
        return true;
    }

    private void LoadFullDeckFromMasterCollection()
    {
        if (masterCardCollection == null)
        {
            Debug.LogError("!!! DeckManagerObject: Master Card Collection atanmamış! Kartlar yüklenemedi.");
            return;
        }

        foreach (var cardData in masterCardCollection.allCards)
        {
            if (cardData != null && !string.IsNullOrEmpty(cardData.id))
            {
                fullDeck.Add(cardData.Clone());
            }
        }

        Debug.Log("✅ Master Deck Yüklendi: " + fullDeck.Count + " kart bulundu.");
    }

    private CardData.CardType ParseCardType(string typeText)
    {
        if (string.IsNullOrEmpty(typeText))
            return CardData.CardType.Attack;

        if (System.Enum.TryParse(typeText, true, out CardData.CardType result))
            return result;

        return CardData.CardType.Attack;
    }

    private string NormalizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        return raw.Trim().ToLowerInvariant().Replace(" ", "_");
    }

    public Sprite GetSpriteByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        string key = NormalizeName(name);

        for (int i = 0; i < characterSprites.Count; i++)
        {
            var cs = characterSprites[i];

            if (cs != null && cs.sprite != null && NormalizeName(cs.characterName) == key)
            {
                Debug.Log("[SPRITE] Listeden bulundu: " + cs.characterName + " -> " + cs.sprite.name);
                return cs.sprite;
            }
        }

        string[] tries = new string[]
        {
            name,
            name.Replace("_", " "),
            name.Replace(" ", "_"),
            name.ToLowerInvariant(),
            name.Replace("_", " ").ToLowerInvariant(),
            name.Replace(" ", "_").ToLowerInvariant()
        };

        for (int i = 0; i < tries.Length; i++)
        {
            string path = "Characters/" + tries[i];
            Debug.Log("[SPRITE LOAD] " + path);

            Sprite sp = Resources.Load<Sprite>(path);

            if (sp != null)
            {
                Debug.Log("[SPRITE OK] " + sp.name);
                return sp;
            }
        }

        Debug.LogWarning("[SPRITE MISS] " + name);
        return null;
    }

    public CardData GetCardById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            Debug.LogWarning("GetCardById boş id ile çağrıldı.");
            return null;
        }

        id = id.Trim();

        // Önce DeckManager'ın zaten bellekte tuttuğu tüm yerel listeler.
        var localLists = new List<List<CardData>>
        {
            currentMatchDeck,
            matchDeck,
            fullDeck,
            deck1,
            deck2,
            deck3,
            deck4,
            deck5
        };

        foreach (var list in localLists)
        {
            CardData found = FindCardInList(list, id);
            if (found != null)
                return found;
        }

        // Ana menüden BattleScene'e taşınan seçili kartlar.
        if (StartBattleManager.Instance != null)
        {
            CardData found = FindCardInList(
                StartBattleManager.Instance.selectedMatchCards, id);

            if (found != null)
                return found;

            found = FindCardInList(
                StartBattleManager.Instance.enemyMatchCards, id);

            if (found != null)
                return found;
        }

        // QR ile edinilip diskte saklanan kişisel envanter.
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv != null)
        {
            CardData found = FindCardInList(inv.myCards, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private CardData FindCardInList(List<CardData> cards, string key)
    {
        if (cards == null)
            return null;

        return cards.FirstOrDefault(c =>
            c != null &&
            (string.Equals(c.id, key, System.StringComparison.Ordinal) ||
             string.Equals(c.cardId, key, System.StringComparison.Ordinal) ||
             string.Equals(c.cardName, key, System.StringComparison.OrdinalIgnoreCase)));
    }

    public void PrepareMatchDeck(List<string> selectedCardIds)
    {
        currentMatchDeck.Clear();

        if (selectedCardIds == null)
        {
            Debug.LogWarning("⚠ PrepareMatchDeck: selectedCardIds null geldi.");
            return;
        }

        foreach (var id in selectedCardIds)
        {
            var c = GetCardById(id);

            if (c != null)
                currentMatchDeck.Add(c);
        }

        Debug.Log("🧩 Maç destesi hazır: " + currentMatchDeck.Count + " kart");
    }

    public List<CardData> GetDeckByIndex(int idx)
    {
        switch (idx)
        {
            case 0: return deck1;
            case 1: return deck2;
            case 2: return deck3;
            case 3: return deck4;
            case 4: return deck5;
            default: return null;
        }
    }

    public List<List<CardData>> GetAllDecks()
    {
        return new List<List<CardData>> { deck1, deck2, deck3, deck4, deck5 };
    }

    public bool AddToDeck(int deckIndex, CardData card, int maxCount = -1)
    {
        var deck = GetDeckByIndex(deckIndex);

        if (deck == null || card == null)
        {
            Debug.LogWarning("Geçersiz deckIndex/kart.");
            return false;
        }

        int limit = maxCount > 0 ? maxCount : deckMaxSize;

        if (deck.Count >= limit)
        {
            Debug.LogWarning("⚠ " + (deckIndex + 1) + ". deste dolu (" + deck.Count + "/" + limit + ")");
            return false;
        }

        string newId = GetCardSaveId(card);

        bool alreadyInDeck = deck.Exists(x =>
            x != null &&
            GetCardSaveId(x) == newId
        );
        

        if (alreadyInDeck)
        {
            Debug.LogWarning("⚠ Bu karakter zaten bu destede var: " + card.cardName);
            return false;
        }

        if (card.characterSprite == null)
            card.characterSprite = GetSpriteByName(card.cardName);

        deck.Add(card);

        SaveDecks();

        Debug.Log("✅ " + card.cardName + " -> " + (deckIndex + 1) + ". deste (" + deck.Count + "/" + limit + ")");

        return true;
    }

    public void SelectDeckForBattle(int deckIndex)
    {
        var deck = GetDeckByIndex(deckIndex);

        if (deck == null)
        {
            Debug.LogWarning("⚠ Geçersiz deste seçimi.");
            return;
        }

        if (deck.Count < deckMinSize)
        {
            Debug.LogWarning("⚠ Oyuna girmek için en az " + deckMinSize + " kart gerekir. Şu an: " + deck.Count);
            return;
        }

        if (deck.Count > deckMaxSize)
        {
            Debug.LogWarning("⚠ Deste limiti aşıldı: " + deck.Count + "/" + deckMaxSize);
            return;
        }

        currentMatchDeck = new List<CardData>(deck);

        Debug.Log("🎯 Savaş için deste seçildi! -> Deste " +
                  (deckIndex + 1) + " (" + currentMatchDeck.Count + "/" + deckMaxSize + ")");
    }

    public List<CardData> GetSelectedCards()
    {
        return currentMatchDeck;
    }
}