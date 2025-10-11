using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MatchResultUI : MonoBehaviour
{
    public static MatchResultUI Instance { get; private set; }

    // Kurulum
    Canvas _canvas;
    RectTransform _root;
    RectTransform _header;
    RectTransform _content;
    RectTransform _footer;

    // Renkler
    readonly Color32 colBg = new Color32(0, 0, 0, 170);
    readonly Color32 colPanel = new Color32(30, 30, 30, 240);
    readonly Color32 colText = new Color32(240, 240, 240, 255);
    readonly Color32 colWin = new Color32(52, 199, 89, 255);
    readonly Color32 colLose = new Color32(255, 69, 58, 255);
    readonly Color32 colSub = new Color32(200, 200, 200, 200);

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.name = "MatchResultUI_Runtime";

        // ⚠️ BuildUI()'i AWAKEDE ÇAĞIRMA - sadece Show()'da oluştur
        // BuildUI(); // BU SATIRI SİL VEYA YORUM YAP
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Dışarıya tek çağrı
    public static void Show()
    {
        if (Instance == null)
        {
            var go = new GameObject("MatchResultUI_Runtime");
            Instance = go.AddComponent<MatchResultUI>();
            Instance.BuildUI(); // ⚠️ UI'ı SADECE BURADA oluştur
        }
        Instance.RefreshAndOpen();
    }

    // UI inşası (tamamen kod)
    void BuildUI()
    {
        // Canvas + Scaler
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        gameObject.AddComponent<GraphicRaycaster>();

        // Arka plan karartma
        _root = CreateUIRect(transform, "Root", anchorMin: Vector2.zero, anchorMax: Vector2.one, sizeDelta: Vector2.zero);
        var bg = _root.gameObject.AddComponent<Image>();
        bg.color = colBg;

        // Ana panel
        var panel = CreateUIRect(_root, "Panel", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
        var img = panel.gameObject.AddComponent<Image>();
        img.color = colPanel;

        // Dikey yerleşim
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.UpperCenter;
        v.padding = new RectOffset(20, 20, 20, 20);
        v.spacing = 12;
        v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = false;

        // Header
        _header = CreateUIRect(panel, "Header", sizeDelta: new Vector2(0, 80));
        // Content (Scroll)
        var scroll = CreateScroll(panel, out _content);
        // Footer
        _footer = CreateUIRect(panel, "Footer", sizeDelta: new Vector2(0, 64));
        _footer.gameObject.SetActive(false);   // ← Ekledik

        var h = _footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = false; h.childControlHeight = false;
        h.spacing = 16;

        // Devam butonu
        var btn = CreateButton(_footer, "Devam", onClick: () => Destroy(gameObject));
        btn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;
    }

    void RefreshAndOpen()
    {
        // Başlık
        ClearChildren(_header);
        var title = CreateTMP(_header, "Title", 36, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;

        bool wasSeason = MatchResultBus.LastWasSeason;
        bool myWin = MatchResultBus.LastWin;

        title.text = myWin ? "🏆 Zafer!" : "❌ Mağlubiyet";
        title.color = myWin ? colWin : colLose;

        // Alt başlık
        var subtitle = CreateTMP(_header, "Sub", 20, FontStyles.Normal);
        subtitle.color = colSub;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.text = wasSeason ? "Sezon maçı" : "Casual maç";

        // Listeyi doldur
        FillList(MatchResultBus.LastCards ?? new List<CardXpDelta>());
    }

    void FillList(List<CardXpDelta> deltas)
    {
        ClearChildren(_content);

        if (deltas == null || deltas.Count == 0)
        {
            var empty = CreateTMP(_content, "Empty", 24, FontStyles.Italic);
            empty.text = "Kart verisi bulunamadı.";
            empty.color = colSub;
            empty.alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (var d in deltas)
            CreateCardRow(_content, d);
    }

    void CreateCardRow(Transform parent, CardXpDelta d)
    {
        var row = CreateUIRect(parent, $"Row_{d.card?.cardName ?? "Card"}", sizeDelta: new Vector2(0, 84));
        var bg = row.gameObject.AddComponent<Image>(); bg.color = new Color32(255, 255, 255, 10);

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(12, 12, 8, 8); h.spacing = 12;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlHeight = true; h.childControlWidth = false;

        // Sol: ikon
        var iconHolder = CreateUIRect(row, "Icon", sizeDelta: new Vector2(72, 72));
        var icon = iconHolder.gameObject.AddComponent<Image>();
        if (d.card != null && d.card.characterSprite != null) icon.sprite = d.card.characterSprite;
        icon.preserveAspect = true;
        icon.color = Color.white;

        // Orta: metinler
        var mid = CreateUIRect(row, "Mid", sizeDelta: new Vector2(0, 0));
        var layout = mid.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true; layout.childControlWidth = true;
        layout.spacing = 2;

        // Ad
        var name = CreateTMP(mid, "Name", 26, FontStyles.Bold);
        name.color = colText;
        name.text = d.card?.cardName?.Replace("_", " ") ?? "(Kart)";

        // Level satırı
        var lv = CreateTMP(mid, "Level", 20, FontStyles.Normal);
        lv.color = colSub;

        // XP satırı
        var xp = CreateTMP(mid, "XP", 20, FontStyles.Normal);
        xp.color = colSub;

        // Sağ: seviye/XP bar (basit)
        var right = CreateUIRect(row, "Right", sizeDelta: new Vector2(180, 48));
        var barBG = right.gameObject.AddComponent<Image>(); barBG.color = new Color32(255, 255, 255, 20);

        var bar = CreateUIRect(right, "Fill", anchorMin: new Vector2(0, 0), anchorMax: new Vector2(0, 1), sizeDelta: new Vector2(0, 0));
        var barImg = bar.gameObject.AddComponent<Image>(); barImg.color = new Color32(80, 180, 255, 255);

        // Veriyi yaz
        int oldL = d.oldLevel, newL = d.newLevel;
        int oldXp = d.oldXp, newXp = d.newXp;

        lv.text = $"Lv {oldL}  →  {newL}";
        int need = Mathf.Max(1, CardLevelSystem.Instance?.XpToNextLevel(newL) ?? 100);
        xp.text = $"XP: {oldXp} → {newXp} / {need}";

        // Basit bar oranı (sadece yeni seviyedeki orana bakıyoruz)
        float t = Mathf.Clamp01((float)newXp / need);
        bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Lerp(0, 180, t));
    }

    // --------------------------------------------------------------------
    // Küçük yardımcılar

    static RectTransform CreateUIRect(Transform parent, string name, Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? sizeDelta = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin ?? new Vector2(0, 0);
        rt.anchorMax = anchorMax ?? new Vector2(1, 1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (sizeDelta.HasValue) rt.sizeDelta = sizeDelta.Value;
        return rt;
    }

    static TextMeshProUGUI CreateTMP(Transform parent, string name, float fontSize, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = fontSize;
        tmp.enableWordWrapping = false;
        tmp.richText = true;
        tmp.text = "";
        tmp.color = Color.white;
        tmp.fontStyle = style;
        var rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.sizeDelta = new Vector2(0, Mathf.CeilToInt(fontSize * 1.4f));
        return tmp;
    }

    static Button CreateButton(Transform parent, string label, System.Action onClick)
    {
        var holder = new GameObject("Button", typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        var rt = holder.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(220, 56);

        var img = holder.AddComponent<Image>();
        img.color = new Color32(60, 120, 255, 255);

        var btn = holder.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        var txt = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        txt.transform.SetParent(holder.transform, false);
        txt.alignment = TextAlignmentOptions.Center;
        txt.font = TMP_Settings.defaultFontAsset;
        txt.fontSize = 24;
        txt.text = label;
        txt.color = Color.white;
        txt.rectTransform.anchorMin = Vector2.zero;
        txt.rectTransform.anchorMax = Vector2.one;
        txt.rectTransform.offsetMin = txt.rectTransform.offsetMax = Vector2.zero;

        return btn;
    }

    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    static RectTransform CreateScroll(Transform parent, out RectTransform content)
    {
        // Scroll kök
        var root = CreateUIRect(parent, "ScrollRoot", sizeDelta: new Vector2(0, 520));
        var img = root.gameObject.AddComponent<Image>(); img.color = new Color32(255, 255, 255, 8);

        // ScrollRect + viewport + content
        var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGO.transform.SetParent(root, false);
        var srt = scrollGO.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = srt.offsetMax = Vector2.zero;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGO.transform, false);
        var vprt = viewport.GetComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = vprt.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color32(0, 0, 0, 0);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(viewport.transform, false);
        content = contentGO.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0, 0);
        content.offsetMax = new Vector2(0, 0);

        var vl = contentGO.GetComponent<VerticalLayoutGroup>();
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        vl.spacing = 8; vl.padding = new RectOffset(8, 8, 8, 8);

        var fit = contentGO.GetComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var sr = scrollGO.GetComponent<ScrollRect>();
        sr.viewport = vprt;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        return (RectTransform)root;
    }
}
