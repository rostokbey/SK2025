using UnityEngine;
using TMPro;

public class SeasonAdminUI : MonoBehaviour
{
    public TMP_InputField seasonIdInput; // boş bırakabilirsin

    // UI Button → OnClick: SeasonAdminUI.NewSeason()
    public void NewSeason()
    {
        string id = (seasonIdInput && !string.IsNullOrWhiteSpace(seasonIdInput.text))
                    ? seasonIdInput.text.Trim()
                    : null;

        SeasonManager.Instance?.StartNewSeason(id);
        Debug.Log($"Yeni sezon başlatıldı: {(id ?? "(auto)")}");
    }
}
