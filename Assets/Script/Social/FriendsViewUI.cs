using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FriendsViewUI : MonoBehaviour
{
    static FriendsViewUI _inst;

    public static void Show()
    {
        if (_inst == null)
        {
            var go = new GameObject("FriendsViewUI");
            _inst = go.AddComponent<FriendsViewUI>();
            DontDestroyOnLoad(go);
        }
        _inst.BuildOrRefresh();
    }

    Canvas canvas;
    RectTransform panel, listRoot, inboxRoot;
    TMP_InputField inviteField;

    void BuildOrRefresh()
    {
        var c = FindObjectOfType<Canvas>();
        if (c == null) { Debug.LogWarning("[FriendsUI] Canvas yok."); return; }
        canvas = c;

        if (panel == null)
        {
            var holder = new GameObject("FriendsPanel");
            holder.transform.SetParent(canvas.transform, false);
            panel = holder.AddComponent<RectTransform>();
            var img = holder.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.6f);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(650, 900);
            panel.anchoredPosition = Vector2.zero;

            // Baþlýk
            var title = CreateTMP(panel, "FRIENDS", 30, new Vector2(0, 400));

            // Invite alaný
            inviteField = CreateInput(panel, "Type a name...", new Vector2(0, 320), new Vector2(500, 60));
            var sendBtn = CreateButton(panel, "INVITE", new Vector2(0, 250), new Vector2(300, 60));
            sendBtn.onClick.AddListener(() =>
            {
                FriendsManager.Instance?.SendInvite(inviteField.text);
                inviteField.text = "";
                BuildLists();
            });

            // List baþlýklarý
            CreateTMP(panel, "Friends", 24, new Vector2(0, 170));
            listRoot = CreateGroup(panel, new Vector2(0, 40), new Vector2(550, 240));

            CreateTMP(panel, "Inbox", 24, new Vector2(0, -40));
            inboxRoot = CreateGroup(panel, new Vector2(0, -210), new Vector2(550, 240));

            // Kapat
            var close = CreateButton(panel, "CLOSE", new Vector2(0, -400), new Vector2(300, 60));
            close.onClick.AddListener(() =>
            {
                Destroy(panel.gameObject);
                panel = null;
            });
        }

        BuildLists();
    }

    void BuildLists()
    {
        ClearChildren(listRoot);
        ClearChildren(inboxRoot);

        // Friends
        var friends = FriendsManager.Instance?.GetFriends();
        if (friends != null)
        {
            foreach (var f in friends)
            {
                var row = CreateRow(listRoot, f.displayName, "REMOVE", () =>
                {
                    FriendsManager.Instance.RemoveFriend(f.displayName);
                    BuildLists();
                });
            }
        }

        // Inbox
        var inbox = FriendsManager.Instance?.GetInbox();
        if (inbox != null)
        {
            for (int i = 0; i < inbox.Count; i++)
            {
                int capture = i;
                var inv = inbox[i];
                var row = CreateRow(inboxRoot, $"{inv.fromName} invited you",
                    "ACCEPT", () => { FriendsManager.Instance.AcceptInvite(capture); BuildLists(); },
                    "DECLINE", () => { FriendsManager.Instance.DeclineInvite(capture); BuildLists(); });
            }
        }
    }

    // ---- helpers ----
    static TextMeshProUGUI CreateTMP(Transform parent, string text, int size, Vector2 anchored)
    {
        var go = new GameObject("TMP");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        rt.sizeDelta = new Vector2(600, 40);
        rt.anchoredPosition = anchored;
        return tmp;
    }

    static TMP_InputField CreateInput(Transform parent, string placeholder, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject("Input");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchored;
        var img = go.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0.1f);

        var textGO = new GameObject("Text"); textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        var textRT = text.GetComponent<RectTransform>(); textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one; textRT.offsetMin = new Vector2(10, 10); textRT.offsetMax = new Vector2(-10, -10);

        var phGO = new GameObject("Placeholder"); phGO.transform.SetParent(go.transform, false);
        var ph = phGO.AddComponent<TextMeshProUGUI>(); ph.text = placeholder; ph.color = new Color(1, 1, 1, 0.5f);
        var phRT = ph.GetComponent<RectTransform>(); phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one; phRT.offsetMin = new Vector2(10, 10); phRT.offsetMax = new Vector2(-10, -10);

        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = textRT;
        input.textComponent = text;
        input.placeholder = ph;
        return input;
    }

    static Button CreateButton(Transform parent, string label, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchored;

        var img = go.AddComponent<Image>(); img.color = new Color(0.15f, 0.5f, 0.8f, 0.9f);
        var btn = go.AddComponent<Button>();

        var t = CreateTMP(go.transform, label, 22, Vector2.zero);
        return btn;
    }

    static RectTransform CreateGroup(Transform parent, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject("Group");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchored;
        return rt;
    }

    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) GameObject.Destroy(t.GetChild(i).gameObject);
    }

    static RectTransform CreateRow(Transform parent, string leftText, string btn1, Action on1, string btn2 = null, Action on2 = null)
    {
        var row = new GameObject("Row").AddComponent<RectTransform>();
        row.transform.SetParent(parent, false);
        row.sizeDelta = new Vector2(540, 48);
        row.anchoredPosition = Vector2.zero;

        var bg = row.gameObject.AddComponent<Image>(); bg.color = new Color(1, 1, 1, 0.06f);

        var label = CreateTMP(row, leftText, 20, new Vector2(-120, 0));
        label.alignment = TextAlignmentOptions.Left;
        label.rectTransform.sizeDelta = new Vector2(360, 40);

        var b1 = CreateButton(row, btn1, new Vector2(180, 0), new Vector2(120, 40));
        b1.onClick.AddListener(() => on1?.Invoke());

        if (!string.IsNullOrEmpty(btn2))
        {
            var b2 = CreateButton(row, btn2, new Vector2(300, 0), new Vector2(120, 40));
            b2.onClick.AddListener(() => on2?.Invoke());
        }

        return row;
    }
}
