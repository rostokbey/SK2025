using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SocialAutoHook : MonoBehaviour
{
    void Start()
    {
        if (FindObjectOfType<FriendsManager>() == null)
            new GameObject("FriendsManager").AddComponent<FriendsManager>();
        if (FindObjectOfType<ClanManager>() == null)
            new GameObject("ClanManager").AddComponent<ClanManager>();

        TryCreateFloatingButtons();
    }

    void TryCreateFloatingButtons()
    {
        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null) { Debug.LogWarning("[SocialAutoHook] Canvas yok."); return; }

        // FR
        CreateMiniButton(canvas.transform, "FR", new Vector2(280, 360)).onClick.AddListener(() =>
        {
            FriendsViewUI.Show();
        });

        // CL
        CreateMiniButton(canvas.transform, "CL", new Vector2(280, 300)).onClick.AddListener(() =>
        {
            ClanViewUI.Show();


        });
    }

    Button CreateMiniButton(Transform parent, string label, Vector2 anchored)
    {
        var holder = new GameObject("Mini_" + label);
        holder.transform.SetParent(parent, false);
        var rt = holder.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(80, 40);
        rt.anchoredPosition = anchored;

        var img = holder.AddComponent<Image>(); img.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        var btn = holder.AddComponent<Button>();
        var text = new GameObject("T").AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(holder.transform, false);
        text.text = label; text.alignment = TextAlignmentOptions.Center; text.fontSize = 20;
        var trt = text.GetComponent<RectTransform>(); trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        return btn;
    }
}
