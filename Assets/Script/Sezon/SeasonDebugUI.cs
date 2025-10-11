using UnityEngine;

public class SeasonDebugUI : MonoBehaviour
{
    public bool show = true;

    void OnGUI()
    {
        if (!show) return;

        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        var profileId = inv != null ? inv.CurrentProfileId : "DEFAULT";

        // Kayıt oluştur/çek (görünen isim olarak da profileId kullan)
        var entry = SeasonRepository.EnsureEntry(profileId, profileId);

        // Toplam kayıt sayısı
        int players = SeasonRepository.Count();

        // Sağ üst bilgi satırı
        string line1 =
            $"Pts:{entry.points}  W:{entry.wins}  L:{entry.losses}  " +
            $"Wstreak:{entry.consecutiveWins}  Lstreak:{entry.consecutiveLosses}  " +
            $"Medals:{entry.medals}  Cups:{entry.cups}  Stars:{entry.stars}  " +
            $"Players:{players}  Eliminated:{entry.eliminated}";

        var w = 900f;
        GUI.Label(new Rect(Screen.width - w - 10, 5, w, 22), line1);

        // F8 → SeasonRepository JSON dump
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F8)
        {
            SeasonRepository.DumpToConsole();
        }
    }
}
