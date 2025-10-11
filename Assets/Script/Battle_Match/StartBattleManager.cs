using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartBattleManager : MonoBehaviour
{
    public static StartBattleManager Instance;

    public List<CardData> selectedMatchCards = new();  // Oyuncu kartları
    public List<CardData> enemyMatchCards = new();     // Düşman kartları

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Sahne geçişlerinde korunur
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void StartBattle()
    {
        DeckManagerObject deckManager = FindObjectOfType<DeckManagerObject>();

        if (deckManager == null)
        {
            Debug.LogError("❌ DeckManagerObject sahnede bulunamadı.");
            return;
        }

        if (deckManager.currentMatchDeck.Count == 0)
        {
            Debug.LogError("❌ Savaş başlamadan önce kart seçilmeli.");
            return;
        }

        // Oyuncu kartlarını kaydet
        selectedMatchCards = new List<CardData>(deckManager.currentMatchDeck);
        Debug.Log($"✅ StartBattleManager: {selectedMatchCards.Count} oyuncu kartı yüklendi.");

        // Düşman kartları oluştur
        enemyMatchCards.Clear();
        foreach (CardData card in selectedMatchCards)
        {
            CardData clone = card.Clone(); // Clone fonksiyonu tanımlı olmalı
            clone.cardName += "_Enemy";
            enemyMatchCards.Add(clone);
        }

        Debug.Log($"🟥 StartBattleManager: {enemyMatchCards.Count} düşman kartı oluşturuldu.");

        // Battle sahnesine geçiş
        SceneManager.LoadScene("BattleScene");
    }
}
