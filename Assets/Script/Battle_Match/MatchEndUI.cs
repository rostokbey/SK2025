using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MatchEndUI : MonoBehaviour
{
    private static MatchEndUI instance;

    private GameObject canvasRoot;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI reasonText;
    private TextMeshProUGUI rewardText;

    private const string MainMenuSceneName = "MainMenu";

    public static void Show(bool isWinner, string reason, string rewardText = "")
    {
        if (instance == null)
        {
            var go = new GameObject("[MatchEndUI]");
            instance = go.AddComponent<MatchEndUI>();
            DontDestroyOnLoad(go);
        }

        instance.BuildIfNeeded();
        instance.ApplyContent(isWinner, reason, rewardText);
    }

    private void BuildIfNeeded()
    {
        if (canvasRoot != null)
            return;

        // Canvas
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRoot = canvasGo;

        // Arka plan
        var bg = new GameObject("Background");
        bg.transform.SetParent(canvasGo.transform, false);
        var bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.92f);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Panel
        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.09f, 0.14f, 0.98f);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(900, 720);
        panelRect.anchoredPosition = Vector2.zero;

        titleText = CreateText(panel.transform, "Title", 88, new Vector2(0, 240), new Vector2(800, 140), FontStyles.Bold);
        reasonText = CreateText(panel.transform, "Reason", 34, new Vector2(0, 100), new Vector2(800, 60));
        rewardText = CreateText(panel.transform, "Reward", 30, new Vector2(0, 10), new Vector2(800, 120));
        rewardText.color = new Color(0.85f, 0.9f, 1f);

        CreateButton(panel.transform, "ANA MENÜYE DÖN", new Vector2(0, -240), GoToMainMenu);
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, float size,
        Vector2 pos, Vector2 sizeDelta, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = style;
        text.color = Color.white;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = pos;
        return text;
    }

    private void CreateButton(Transform parent, string label, Vector2 pos, System.Action onClick)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.55f, 0.9f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420, 100);
        rect.anchoredPosition = pos;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 40;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    private void ApplyContent(bool isWinner, string reason, string reward)
    {
        canvasRoot.SetActive(true);

        titleText.text = isWinner ? "KAZANDIN" : "KAYBETTİN";
        titleText.color = isWinner
            ? new Color(0.3f, 1f, 0.4f)
            : new Color(1f, 0.4f, 0.4f);

        reasonText.text = GetReasonText(reason, isWinner);
        rewardText.text = reward;
    }

    private string GetReasonText(string reason, bool isWinner)
    {
        switch (reason)
        {
            case "surrender":
                return isWinner ? "Rakip teslim oldu" : "Teslim oldun";

            case "victory":
                return isWinner ? "Tüm rakip karakterler düştü" : "Tüm karakterlerin düştü";

            case "team_disconnected":
                return isWinner ? "Rakip takımın bağlantısı koptu" : "Takım arkadaşının bağlantısı koptu";

            case "host_disconnected":
                return isWinner ? "Oda sahibi oyundan ayrıldı" : "Oda sahibi oyundan ayrıldı";

            case "afk_timeout":
                return isWinner ? "Rakip hamle süresini aştı" : "Hamle süren doldu";

            case "initial_placement_timeout":
                return isWinner ? "Rakip yerleşim süresini aştı" : "Yerleşim süren doldu";

            default:
                return isWinner ? "Maç kazanıldı" : "Maç kaybedildi";
        }
    }

    private void GoToMainMenu()
    {
        if (canvasRoot != null)
            canvasRoot.SetActive(false);

        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
            nm.Shutdown();

        SceneManager.LoadScene(MainMenuSceneName);
    }
}