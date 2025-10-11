using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DeckManagerObject : MonoBehaviour
{
    // ------------ Singleton ------------
    public static DeckManagerObject Instance;

    [Header("Deste Ayarları")]
    public int deckMaxSize = 2;

    // YENİ: Ana kart listesini tutan ScriptableObject referansı
    [Header("Veri Kaynağı")]
    public CardCollection masterCardCollection;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // YENİ: Oyuna başlarken ana kart listesini yükle
        LoadFullDeckFromCollection();
    }

    // YENİ: Kartları CardCollection asset'inden alan fonksiyon
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
                // Her kartın klonunu alarak ekliyoruz. Bu, ana asset'teki veriyi korur.
                fullDeck.Add(cardData.Clone());
            }
        }
        Debug.Log($"✅ Master Deck Yüklendi: {fullDeck.Count} kart bulundu.");
    }

    // ------------ Veriler ------------
    public List<CardData> fullDeck = new();
    public List<CardData> currentMatchDeck = new();
    public List<CardData> matchDeck = new();  // kullanıyorsan dursun
    public List<CardData> deck1 = new();
    public List<CardData> deck2 = new();
    public List<CardData> deck3 = new();
    public List<CardData> deck4 = new();
    public List<CardData> deck5 = new();

    [System.Serializable]
    public class CharacterSprite { public string characterName; public Sprite sprite; }
    public List<CharacterSprite> characterSprites = new();

    // ------------ Yardımcılar ------------
    private string NormalizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant().Replace(" ", "_");
    }

    public Sprite GetSpriteByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

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
            name.Replace("_"," "),
            name.Replace(" ","_"),
            name.ToLowerInvariant(),
            name.Replace("_"," ").ToLowerInvariant(),
            name.Replace(" ","_").ToLowerInvariant()
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

    // ------------ KART BULMA ------------
    public CardData GetCardById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("❌ GetCardById boş id ile çağrıldı.");
            return null;
        }

        // Önce fullDeck’te ara
        var card = fullDeck.FirstOrDefault(c => c != null && c.id == id);

        // Bulunamadıysa PlayerInventory’den dene
        if (card == null)
        {
            var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
            if (inv != null)
                card = inv.myCards.FirstOrDefault(c => c != null && c.id == id);
        }

        if (card == null)
            Debug.LogError($"❌ Spawn edilecek kart bulunamadı: {id}");
        else
            Debug.Log($"[DeckManager] Kart bulundu: {card.cardName} ({card.id})");

        return card;
    }

    public void PrepareMatchDeck(List<string> selectedCardIds)
    {
        currentMatchDeck.Clear();
        if (selectedCardIds == null) return;
        foreach (var id in selectedCardIds)
        {
            var c = GetCardById(id);
            if (c != null) currentMatchDeck.Add(c);
        }
        Debug.Log("🧩 Maç destesi hazır: " + currentMatchDeck.Count + " kart");
    }

    // ------------ Deste işlemleri (0..4) ------------
    public List<CardData> GetDeckByIndex(int idx) => idx switch
    {
        0 => deck1,
        1 => deck2,
        2 => deck3,
        3 => deck4,
        4 => deck5,
        _ => null
    };

    public List<List<CardData>> GetAllDecks() =>
        new() { deck1, deck2, deck3, deck4, deck5 };

    public bool AddToDeck(int deckIndex, CardData card, int maxCount = -1)
    {
        var deck = GetDeckByIndex(deckIndex);
        if (deck == null || card == null) { Debug.LogWarning("Geçersiz deckIndex/kart."); return false; }

        int limit = (maxCount > 0) ? maxCount : deckMaxSize;
        if (deck.Count >= limit) { Debug.LogWarning($"⚠ {deckIndex + 1}. deste dolu ({deck.Count}/{limit})"); return false; }

        if (card.characterSprite == null)
            card.characterSprite = GetSpriteByName(card.cardName);

        deck.Add(card);
        Debug.Log($"✅ {card.cardName} -> {deckIndex + 1}. deste ({deck.Count}/{limit})");
        return true;
    }

    public void SelectDeckForBattle(int deckIndex)
    {
        var deck = GetDeckByIndex(deckIndex);
        if (deck == null || deck.Count != deckMaxSize)
        {
            Debug.LogWarning($"⚠ Lütfen {deckMaxSize} kartlık bir deste seçin.");
            return;
        }
        currentMatchDeck = deck;
        Debug.Log($"🎯 Savaş için deste seçildi! -> Deste {deckIndex + 1} ({deck.Count}/{deckMaxSize})");
    }

    public List<CardData> GetSelectedCards() => currentMatchDeck;
}
