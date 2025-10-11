using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckSelectController : MonoBehaviour
{
    [Header("Refs")]
    public DeckManagerObject deckManager;
    public Button[] deckButtons;          // 1..5 “Deste” butonların
    public Button readyButton;            // HAZIRIM
    public TMP_Text feedbackText;         // opsiyonel uyarı/log alanı

    [Header("Ayarlar")]
    public int minCardsToFight = 2;       // test için 2; istersen 25 yap

    private int selectedDeckIndex = -1;   // 0..4

    void Awake()
    {
        if (deckManager == null) deckManager = FindObjectOfType<DeckManagerObject>();

        // Butonlara click bağla (Inspector’dan da verebilirsin)
        if (deckButtons != null)
        {
            for (int i = 0; i < deckButtons.Length; i++)
            {
                int idx = i;
                deckButtons[i].onClick.RemoveAllListeners();
                deckButtons[i].onClick.AddListener(() => OnClickSelectDeck(idx));
            }
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnClickReady);
        }
    }

    public void OnClickSelectDeck(int deckIdx)
    {
        selectedDeckIndex = deckIdx;

        // ufak görsel geri bildirim (buton renkleri vs. istersen ekleyebilirsin)
        if (feedbackText) feedbackText.text = $"Deste {deckIdx + 1} seçildi.";

        // sayıyı da göstermek istersek:
        int count = GetDeckCount(deckIdx);
        Debug.Log($"🟢 Deste {deckIdx + 1} seçildi. Kart sayısı: {count}");
    }

    public void OnClickReady()
    {
        if (selectedDeckIndex < 0)
        {
            Warn("Önce bir deste seçmelisin.");
            return;
        }

        int count = GetDeckCount(selectedDeckIndex);
        if (count < minCardsToFight)
        {
            Warn($"Bu destede {count} kart var. En az {minCardsToFight} olmalı.");
            return;
        }

        // Seçilen desteyi maç için kaydet
        deckManager.SelectDeckForBattle(selectedDeckIndex);
        Info($"✅ Deste {selectedDeckIndex + 1} kaydedildi. Savaşa hazırsın!");

        // Burada sadece seçimi kaydediyoruz. Savaşı başlatmak için
        // mevcut “SAVAŞ BAŞLAT” butonun zaten StartBattleManager.StartBattle() çağırıyor.
        // İstersen burada direkt sahneye geçiş de yapabilirsin:
        // StartBattleManager.Instance?.StartBattle();
    }

    private int GetDeckCount(int idx)
    {
        switch (idx)
        {
            case 0: return deckManager.deck1.Count;
            case 1: return deckManager.deck2.Count;
            case 2: return deckManager.deck3.Count;
            case 3: return deckManager.deck4.Count;
            case 4: return deckManager.deck5.Count;
        }
        return 0;
    }

    private void Warn(string msg)
    {
        Debug.LogWarning(msg);
        if (feedbackText) feedbackText.text = msg;
    }

    private void Info(string msg)
    {
        Debug.Log(msg);
        if (feedbackText) feedbackText.text = msg;
    }
}