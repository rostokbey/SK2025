using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ana menüdeki SettingPanel'i doldurur; BattleScene'de aynı ayarları kullanan
/// MENÜ düğmesini ve güvenli savaştan çıkış işlemini otomatik oluşturur.
/// Online maçta Time.timeScale değiştirilmez; yalnızca yerel menü açılır.
/// </summary>
public class SharedSettingsMenu : MonoBehaviour
{
    private const string ContentName = "SharedSettingsContent";
    private static Font runtimeFont;

    private GameObject panelRoot;
    private Text soundText;
    private Text musicText;
    private Text vibrationText;
    private bool leavingBattle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        InstallForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => InstallForScene(scene);

    private static void InstallForScene(Scene scene)
    {
        if (Object.FindFirstObjectByType<SharedSettingsMenu>(FindObjectsInactive.Include) != null)
            return;

        if (scene.name == "AnaSahne")
        {
            GameObject existingPanel = FindSceneObject("SettingPanel");
            if (existingPanel != null)
            {
                SharedSettingsMenu menu = existingPanel.AddComponent<SharedSettingsMenu>();
                menu.BuildInsideExistingPanel(existingPanel);
            }
        }
        else if (scene.name == "BattleScene")
        {
            GameObject host = new GameObject("BattleSharedSettingsMenu");
            SharedSettingsMenu menu = host.AddComponent<SharedSettingsMenu>();
            menu.BuildBattleMenu();
        }

        SharedGameSettings.Apply();
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform item in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (item != null && item.name == objectName && item.gameObject.scene.IsValid())
                return item.gameObject;
        }
        return null;
    }

    private void BuildInsideExistingPanel(GameObject existingPanel)
    {
        panelRoot = existingPanel;
        if (existingPanel.transform.Find(ContentName) == null)
            BuildContent(existingPanel.transform, includeExitBattle: false);
        RefreshLabels();
    }

    private void BuildBattleMenu()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        Button menuButton = CreateButton(transform, "BattleMenuButton", "MENÜ", new Vector2(150, 70));
        RectTransform menuRect = menuButton.GetComponent<RectTransform>();
        menuRect.anchorMin = menuRect.anchorMax = new Vector2(1f, 1f);
        menuRect.pivot = new Vector2(1f, 1f);
        menuRect.anchoredPosition = new Vector2(-25f, -25f);

        panelRoot = CreatePanel(transform, "BattleSettingsPanel");
        BuildContent(panelRoot.transform, includeExitBattle: true);
        // Layout içindeki KAPAT küçük ekranlarda aşağı taşsa bile erişilebilen
        // sabit bir X düğmesi bulunur. MENÜ düğmesi de aç/kapat şeklinde çalışır.
        Button fixedClose = CreateButton(panelRoot.transform, "FixedCloseButton", "X", new Vector2(80, 70));
        RectTransform closeRect = fixedClose.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-20f, -20f);
        fixedClose.transform.SetAsLastSibling();
        fixedClose.onClick.AddListener(CloseMenu);

        panelRoot.SetActive(false);
        menuButton.onClick.AddListener(() => panelRoot.SetActive(!panelRoot.activeSelf));

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void BuildContent(Transform parent, bool includeExitBattle)
    {
        GameObject content = CreatePanel(parent, ContentName);
        RectTransform rect = content.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(620f, includeExitBattle ? 760f : 620f);
        rect.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(35, 35, 35, 35);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        Text title = CreateText(content.transform, "AYARLAR", 42);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 75f;

        Button sound = CreateButton(content.transform, "SoundButton", "", new Vector2(520, 82));
        soundText = sound.GetComponentInChildren<Text>();
        sound.onClick.AddListener(() => { SharedGameSettings.SoundEnabled = !SharedGameSettings.SoundEnabled; RefreshLabels(); });

        Button music = CreateButton(content.transform, "MusicButton", "", new Vector2(520, 82));
        musicText = music.GetComponentInChildren<Text>();
        music.onClick.AddListener(() => { SharedGameSettings.MusicEnabled = !SharedGameSettings.MusicEnabled; RefreshLabels(); });

        Button vibration = CreateButton(content.transform, "VibrationButton", "", new Vector2(520, 82));
        vibrationText = vibration.GetComponentInChildren<Text>();
        vibration.onClick.AddListener(() => { SharedGameSettings.VibrationEnabled = !SharedGameSettings.VibrationEnabled; RefreshLabels(); });

        if (includeExitBattle)
        {
            Button exit = CreateButton(content.transform, "ExitBattleButton", "SAVAŞTAN ÇIK", new Vector2(520, 92));
            exit.GetComponent<Image>().color = new Color(0.65f, 0.08f, 0.08f, 0.98f);
            exit.onClick.AddListener(ExitBattle);
        }

        Button close = CreateButton(content.transform, "CloseSettingsButton", "KAPAT", new Vector2(520, 82));
        close.onClick.AddListener(CloseMenu);
        RefreshLabels();
    }

    private void Update()
    {
        if (panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            CloseMenu();
    }

    private void CloseMenu()
    {
        if (SceneManager.GetActiveScene().name == "AnaSahne")
        {
            SceneUIController controller = Object.FindFirstObjectByType<SceneUIController>();
            if (controller != null) controller.GoToAnamenu();
            else if (panelRoot != null) panelRoot.SetActive(false);
        }
        else if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void ExitBattle()
    {
        if (leavingBattle) return;
        leavingBattle = true;

        BattleManager battle = BattleManager.Instance;
        if (battle != null && battle.IsSpawned)
            battle.RequestSurrenderServerRpc();

        StartCoroutine(LeaveBattleRoutine());
    }

    private IEnumerator LeaveBattleRoutine()
    {
        yield return new WaitForSecondsRealtime(0.25f);
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene("AnaSahne");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (leavingBattle || NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;
        leavingBattle = true;
        SceneManager.LoadScene("AnaSahne");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void RefreshLabels()
    {
        if (soundText != null) soundText.text = "SES: " + (SharedGameSettings.SoundEnabled ? "AÇIK" : "KAPALI");
        if (musicText != null) musicText.text = "MÜZİK: " + (SharedGameSettings.MusicEnabled ? "AÇIK" : "KAPALI");
        if (vibrationText != null) vibrationText.text = "TİTREŞİM: " + (SharedGameSettings.VibrationEnabled ? "AÇIK" : "KAPALI");
    }

    private static GameObject CreatePanel(Transform parent, string name)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.07f, 0.94f);
        return panel;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = size;
        go.GetComponent<LayoutElement>().preferredHeight = size.y;
        go.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.42f, 0.98f);

        Text text = CreateText(go.transform, label, 30);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        return go.GetComponent<Button>();
    }

    private static Text CreateText(Transform parent, string value, int size)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.text = value;
        text.font = GetFont();
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        return text;
    }

    private static Font GetFont()
    {
        if (runtimeFont == null)
            runtimeFont = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return runtimeFont;
    }
}
