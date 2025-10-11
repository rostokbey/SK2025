using UnityEngine;
using System.Linq;

public static class SeasonAutoLog
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Dump()
    {
        try
        {
            var inv = PlayerInventory.Instance ?? Object.FindObjectOfType<PlayerInventory>();
            string pid = inv?.CurrentProfileId ?? "DEFAULT";

            var entry = SeasonRepository.GetEntry(pid);
            int rank = SeasonRepository.GetRank(pid);

            Debug.Log(
                $"[Season] pid={pid}  " +
                $"Pts={entry?.points ?? 0}, W={entry?.wins ?? 0}, L={entry?.losses ?? 0}, " +
                $"StreakW={entry?.consecutiveWins ?? 0}, StreakL={entry?.consecutiveLosses ?? 0}, " +
                $"Medals={entry?.medals ?? 0}, Cups={entry?.cups ?? 0}, Stars={entry?.stars ?? 0}, " +
                $"Rank={(rank > 0 ? rank : -1)}"
            );

            var top = SeasonRepository.GetTop(10);
            for (int i = 0; i < top.Count; i++)
                Debug.Log($"[Season][TOP {i + 1}] {top[i].displayName}  Pts={top[i].points}  W={top[i].wins}  L={top[i].losses}");

            Debug.Log($"[Season] Eliminated? {(SeasonManager.Instance?.IsEliminated() ?? false)}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Season] auto log failed: " + ex.Message);
        }
    }
}
