// SafeAreaFitter.cs
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt;
    Rect last;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        if (!rt) { Debug.LogError("SafeAreaFitter: RectTransform yok!"); enabled = false; return; }
        Apply();
    }

    void OnEnable() { Apply(); }
    void OnRectTransformDimensionsChange() { Apply(); }

    public void Apply()
    {
        if (!rt) return;

        var sa = Screen.safeArea;
        if (sa.Equals(last)) return;
        last = sa;

        Vector2 min = sa.position;
        Vector2 max = sa.position + sa.size;
        min.x /= Screen.width; min.y /= Screen.height;
        max.x /= Screen.width; max.y /= Screen.height;

        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
