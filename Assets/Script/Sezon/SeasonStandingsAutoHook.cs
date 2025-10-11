using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SeasonStandingsAutoHook : MonoBehaviour
{
    void Start()
    {
        // Tek kopya
        if (FindObjectOfType<SeasonStandingsView>() == null)
            new GameObject("SeasonStandingsView").AddComponent<SeasonStandingsView>();

        // Küçük “TOP” butonu (sað-üst)
        TryCreateFloatingButton();
    }

    void Update()
    {
        // test: F2 ile aç/kapa
        if (Input.GetKeyDown(KeyCode.F2))
        {
            if (SeasonStandingsView.Instance && SeasonStandingsView.Instance.gameObject.activeSelf)
                SeasonStandingsView.Instance.SetVisible(false);
            else
                SeasonStandingsView.Instance?.SetVisible(true);
        }
    }

    void TryCreateFloatingButton()
    {
        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        var holder = new GameObject("SeasonTopBtn");
        var rt = holder.AddComponent<RectTransform>();
        holder.transform.SetParent(canvas.transform, false);
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-24, -24);
        rt.sizeDelta = new Vector2(90, 46);

        var img = holder.AddComponent<Image>();
        img.color = new Color(0.12f, 0.6f, 0.95f);

        var btn = holder.AddComponent<Button>();
        btn.onClick.AddListener(() => {
            var v = SeasonStandingsView.Instance ?? FindObjectOfType<SeasonStandingsView>(true);
            if (v != null) v.SetVisible(!v.gameObject.activeSelf);
        });


        var label = new GameObject("Label").AddComponent<TextMeshProUGUI>();
        label.transform.SetParent(holder.transform, false);
        label.text = "TOP";
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 24;
        label.color = Color.white;
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
    }
}
