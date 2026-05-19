using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DeckManagerObject : MonoBehaviour
{
    public static DeckManagerObject Instance;

    [Header("Deste Ayarları")]
    public int deckMinSize = 5;
    public int deckMaxSize = 25;
    public int battlePlayableLimit = 12;

    [Header("Veri Kaynağı")]
    public CardCollection masterCardCollection;

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
    }

    private void LoadFullDeckFromCollection()
    {
        fullDeck.Clear();

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
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("❌ GetCardById boş id ile çağrıldı.");
            return null;
        }

        var card = fullDeck.FirstOrDefault(c => c != null && c.id == id);

        if (card == null)
        {
            var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();

            if (inv != null)
                card = inv.myCards.FirstOrDefault(c => c != null && c.id == id);
        }

        if (card == null)
            Debug.LogError("❌ Spawn edilecek kart bulunamadı: " + id);
        else
            Debug.Log("[DeckManager] Kart bulundu: " + card.cardName + " (" + card.id + ")");

        return card;
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

        bool alreadyInDeck = deck.Exists(x =>
            x != null &&
            x.cardName == card.cardName
        );

        if (alreadyInDeck)
        {
            Debug.LogWarning("⚠ Bu karakter zaten bu destede var: " + card.cardName);
            return false;
        }

        if (card.characterSprite == null)
            card.characterSprite = GetSpriteByName(card.cardName);

        deck.Add(card);

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