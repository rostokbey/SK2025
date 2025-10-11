using UnityEngine;
using System.Text;

public class SeasonStandingUI : MonoBehaviour
{
    [ContextMenu("Print Top 10 To Console")]
    public void PrintTop10()
    {
        var top = SeasonRepository.GetTop(10);
        var sb = new StringBuilder();
        sb.AppendLine("==== Top 10 ====");
        for (int i = 0; i < top.Count; i++)
            sb.AppendLine($"{i + 1}. {top[i].displayName} — {top[i].points}p (W:{top[i].wins} / L:{top[i].losses})");
        Debug.Log(sb.ToString());

        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv != null)
        {
            int myRank = SeasonRepository.GetRank(inv.CurrentProfileId ?? "DEFAULT");
            Debug.Log($"[Season] Benim sýram: {myRank}");
        }
    }
}
