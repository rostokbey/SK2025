using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class MatchReadyButton : MonoBehaviour
{
    public Button readyButton;

    void Start()
    {
        if (readyButton != null)
        {
            readyButton.onClick.AddListener(OnReadyClicked);
        }
        else
        {
            Debug.LogError("Ready button atanmadı!");
        }
    }

    void OnReadyClicked()
    {
        var deckManager = FindObjectOfType<DeckManagerObject>();
        if (deckManager == null)
        {
            Debug.LogError("❌ DeckManagerObject bulunamadı!");
            return;
        }

        List<string> selectedIds = new List<string>();
        foreach (var card in deckManager.fullDeck)
        {
            selectedIds.Add(card.id);
        }

        deckManager.PrepareMatchDeck(selectedIds);

        Debug.Log("✅ MatchReadyButton -> PrepareMatchDeck çağrıldı.");
    }
}
