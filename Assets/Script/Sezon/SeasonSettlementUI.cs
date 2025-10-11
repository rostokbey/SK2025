// Assets/Script/Sezon/SeasonSettlementUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SeasonSettlementUI : MonoBehaviour
{
    public static SeasonSettlementUI Instance { get; private set; }

    Canvas canvas;
    RectTransform panel;
    TextMeshProUGUI title, body;
    Button closeBtn;

    void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    static Canvas FindOrCreateCanvas()
    {
        var c = Object.FindObjectOfType<Canvas>();
        if (c != null) return c;

        var go = new GameObject("Canvas(Runtime)");
        c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    void BuildUI()
    {
        canvas = FindOrCreateCanvas();

        var hold = new GameObject("SeasonSettlementPanel");
        hold.transform.SetParent(canvas.transform, false);
        panel = hold.AddComponent<RectTransform>();
        var img = hold.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.75f);

        panel.anchorMin = new Vector2(0, 0);
        panel.anchorMax = new Vector2(1, 1);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        // Card
        var card = new GameObject("Card");
        card.transform.SetParent(panel, false);
        var cardRT = card.AddComponent<RectTransform>();
        var cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);
        cardRT.sizeDelta = new Vector2(640, 780);
        cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;

        title = CreateLabel(card.transform, "Sezon Tamamlandý!", 32, FontStyles.Bold);
        var tRT = title.rectTransform;
        tRT.anchoredPosition = new Vector2(0, 300);

        body = CreateLabel(card.transform, "", 24, FontStyles.Normal);
        var bRT = body.rectTransform;
        bRT.sizeDelta = new Vector2(580, 560);
        bRT.anchoredPosition = new Vector2(0, -30);
        body.alignment = TextAlignmentOptions.TopLeft;

        closeBtn = CreateButton(card.transform, "TAMAM");
        var cRT = closeBtn.GetComponent<RectTransform>();
        cRT.anchoredPosition = new Vector2(0, -320);
        closeBtn.onClick.AddListener(Hide);

        gameObject.SetActive(false);
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string txt, int size, FontStyles style)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(600, 120);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = txt;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    static Button CreateButton(Transform parent, string txt)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(260, 80);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.35f, 0.9f, 1f);

        var btn = go.AddComponent<Button>();

        var lbl = CreateLabel(go.transform, txt, 26, FontStyles.Bold);
        lbl.alignment = TextAlignmentOptions.Center;

        return btn;
    }

    public static void Show()
    {
        if (Instance == null)
        {
            var go = new GameObject("SeasonSettlementUI(Runtime)");
            Instance = go.AddComponent<SeasonSettlementUI>();
        }
        Instance.Refresh();
        Instance.gameObject.SetActive(true);
    }

    public static void Hide()
    {
        if (Instance != null) Instance.gameObject.SetActive(false);
    }

    void Refresh()
    {
        var s = SeasonBus.LastSettle;

        // satýr satýr info hazýrla
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Katýlýmcý sayýsý : {s.participants}");
        sb.AppendLine($"Reset puaný     : {s.resetPoints}");
        sb.AppendLine("");

        if (s.hadChampion)
        {
            sb.AppendLine($"Þampiyon        : {s.championName}");
            sb.AppendLine($"Verilen madalya : +{s.medalsDelta}  (yeni toplam: {s.championMedalsAfter})");
        }
        else
        {
            sb.AppendLine("Bu sezon için þampiyon bulunamadý.");
        }

        if (!string.IsNullOrEmpty(s.note))
        {
            sb.AppendLine("");
            sb.AppendLine(s.note);
        }

        body.text = sb.ToString();
    }
}
