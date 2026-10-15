using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Savaş sahnesindeki ekran Canvas'ını güvenilir biçimde bulur:
/// yalnızca kök (iç içe olmayan), WorldSpace olmayan Canvas'lar aranır;
/// aktif olanlar ve yüksek sortingOrder'a sahip olanlar öne alınır.
/// </summary>
public static class BattleCanvasUtility
{
    public static Canvas FindScreenCanvas(Scene scene)
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        return canvases
            .Where(c => c != null &&
                        c.gameObject.scene == scene &&
                        c.renderMode != RenderMode.WorldSpace &&
                        IsRootCanvas(c))
            .OrderByDescending(c => c.gameObject.activeInHierarchy)
            .ThenByDescending(c => c.sortingOrder)
            .FirstOrDefault();
    }

    private static bool IsRootCanvas(Canvas canvas)
    {
        Transform parent = canvas.transform.parent;
        return parent == null || parent.GetComponentInParent<Canvas>(true) == null;
    }
}
