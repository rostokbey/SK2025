using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Yerel tıklama/dokunma ile saldırgan ve düşman hedefi seçer.
/// Seçim istemcide yapılır; saldırgan sahipliği, sıra ve ön hat koruması
/// BattleManager tarafından sunucuda yeniden doğrulanır.
/// </summary>
public sealed class BattleActionSelectionUI : MonoBehaviour
{
    private BattleManager battleManager;
    private Character selectedAttacker;
    private Character selectedTarget;
    private Character inspectedCharacter;

    private RectTransform panelRect;
    private TMP_Text statusText;
    private TMP_Text infoText;
    private Button attackButton;
    private Button skillButton;

    public void Initialize(
        BattleManager manager,
        Button existingAttackButton = null,
        Button existingSkillButton = null)
    {
        battleManager = manager;
        attackButton = existingAttackButton;
        skillButton = existingSkillButton;

        EnsureRuntimeUI();
        BindButtons();
        RefreshUI();
    }

    private void BindButtons()
    {
        if (attackButton != null)
        {
            attackButton.onClick.RemoveAllListeners();
            attackButton.onClick.AddListener(() => TryRequestAttack());
        }

        if (skillButton != null)
        {
            skillButton.onClick.RemoveAllListeners();
            skillButton.onClick.AddListener(() => TryRequestActiveAbility());
        }
    }

    private void Update()
    {
        if (battleManager == null)
            return;

        bool combatVisible = battleManager.Phase.Value == BattlePhase.PlayerTurn;
        if (!combatVisible && panelRect != null && panelRect.gameObject.activeSelf)
            panelRect.gameObject.SetActive(false);

        if (!combatVisible)
            return;

        TrySelectCharacterFromPointer();
        ValidateSelection();
        RefreshUI();
    }

    public bool TryRequestAttack()
    {
        if (!CanUseSelection(out string reason))
        {
            SetStatus(reason);
            return false;
        }

        if (!IsFrontmostTargetInColumn(selectedTarget))
        {
            SetStatus("Bu hedef ön sıradaki karakter tarafından korunuyor.");
            return false;
        }

        battleManager.SendSelectedAttackServerRpc(
            selectedAttacker.NetworkObjectId,
            selectedTarget.NetworkObjectId);
        SetStatus("Saldırı isteği sunucuya gönderildi.");
        return true;
    }

    public bool TryRequestActiveAbility()
    {
        if (!CanUseSelection(out string reason))
        {
            SetStatus(reason);
            return false;
        }

        if (!selectedAttacker.IsActiveAbilityReady)
        {
            SetStatus(
                $"Yetenek hazır değil: {selectedAttacker.AbilityCooldownRemaining.Value} raund kaldı.");
            return false;
        }

        selectedAttacker.UseActiveAbilityServerRpc(selectedTarget.gridColumn);
        SetStatus("Yetenek isteği sunucuya gönderildi.");
        return true;
    }

    private bool CanUseSelection(out string reason)
    {
        reason = null;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            reason = "Ağ bağlantısı hazır değil.";
            return false;
        }

        if (battleManager.Phase.Value != BattlePhase.PlayerTurn ||
            battleManager.ActivePlayerClientId.Value != NetworkManager.Singleton.LocalClientId)
        {
            reason = "Şu anda sıra sende değil.";
            return false;
        }

        if (selectedAttacker == null || !selectedAttacker.IsAlive)
        {
            reason = "Önce kendi canlı karakterlerinden birini seç.";
            return false;
        }

        if (!battleManager.IsCharacterControlledByLocalPlayer(selectedAttacker))
        {
            reason = "Yalnızca kendi karakterinle saldırabilirsin.";
            return false;
        }

        if (selectedTarget == null || !selectedTarget.IsAlive)
        {
            reason = "Saldırmak istediğin canlı düşmanı seç.";
            return false;
        }

        if (selectedAttacker.isPlayer == selectedTarget.isPlayer)
        {
            reason = "Takım arkadaşın hedef seçilemez.";
            return false;
        }

        return true;
    }

    private void TrySelectCharacterFromPointer()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        if (CharacterPlacer.Instance != null && CharacterPlacer.Instance.IsPlacementMode)
            return;

        // BattleScene'de tam ekran UI arka planı bulunduğu için
        // EventSystem.IsPointerOverGameObject her saha dokunuşunda true
        // dönüyordu ve 3D raycast hiç çalışmıyordu. Yalnızca gerçekten
        // etkileşimli olan seçim/ayar panelinin üzerindeki dokunuşu engelle.
        if (panelRect != null &&
            panelRect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(
                panelRect,
                Input.mousePosition,
                GetCanvasCamera(panelRect)))
            return;

        GameObject settingsPanel = GameObject.Find("BattleSettingsPanel");
        if (settingsPanel != null && settingsPanel.activeInHierarchy)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Character character = hit.collider.GetComponentInParent<Character>();
            if (character == null)
                continue;

            inspectedCharacter = character;
            if (panelRect != null)
                panelRect.gameObject.SetActive(true);

            if (!character.IsAlive)
            {
                SetStatus("Bu karakter düşmüş durumda.");
                return;
            }

            if (battleManager.IsCharacterOnLocalPlayersTeam(character))
            {
                if (battleManager.IsCharacterControlledByLocalPlayer(character))
                {
                    selectedAttacker = character;
                    SetStatus($"Saldırgan seçildi: {GetDisplayName(character)}");
                }
                else
                {
                    SetStatus("Takım arkadaşının karakter bilgileri gösteriliyor.");
                }
            }
            else
            {
                selectedTarget = character;
                SetStatus(IsFrontmostTargetInColumn(character)
                    ? $"Hedef seçildi: {GetDisplayName(character)}"
                    : "Hedef bilgileri gösteriliyor; ön sıradaki koruyucu önce yenilmeli.");
            }

            return;
        }
    }

    private bool IsFrontmostTargetInColumn(Character target)
    {
        if (target == null || target.gridColumn < 0)
            return false;

        Character[] characters = Object.FindObjectsByType<Character>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (Character other in characters)
        {
            if (other == null || other == target || !other.IsAlive)
                continue;

            if (other.isPlayer == target.isPlayer &&
                other.gridColumn == target.gridColumn &&
                (int)other.gridRow < (int)target.gridRow)
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateSelection()
    {
        if (selectedAttacker != null &&
            (!selectedAttacker.IsAlive ||
             !battleManager.IsCharacterControlledByLocalPlayer(selectedAttacker)))
        {
            selectedAttacker = null;
        }

        if (selectedTarget != null &&
            (!selectedTarget.IsAlive ||
             battleManager.IsCharacterOnLocalPlayersTeam(selectedTarget)))
        {
            selectedTarget = null;
        }

        if (inspectedCharacter != null && !inspectedCharacter.IsSpawned)
            inspectedCharacter = null;
    }

    private void RefreshUI()
    {
        bool isMyTurn =
            NetworkManager.Singleton != null &&
            battleManager != null &&
            battleManager.Phase.Value == BattlePhase.PlayerTurn &&
            battleManager.ActivePlayerClientId.Value == NetworkManager.Singleton.LocalClientId;

        bool usable =
            isMyTurn &&
            selectedAttacker != null &&
            selectedAttacker.IsAlive &&
            selectedTarget != null &&
            selectedTarget.IsAlive;

        if (attackButton != null)
            attackButton.interactable = usable;

        if (skillButton != null)
            skillButton.interactable = usable && selectedAttacker.IsActiveAbilityReady;

        if (infoText != null)
        {
            infoText.text = inspectedCharacter == null
                ? "Bilgi için bir karaktere dokun."
                : BuildCharacterInfo(inspectedCharacter);
        }

        if (statusText == null)
            return;

        // Karakter seçilmeden ekranda sürekli yönerge gösterme. Panel ilk
        // karakter dokunuşuna kadar kapalı kalır.
    }

    private string BuildCharacterInfo(Character character)
    {
        HealthSystem health = character.GetComponent<HealthSystem>();
        int currentHp = health != null ? health.currentHP.Value : 0;
        int maxHp = character.MaxHpNet.Value > 0
            ? character.MaxHpNet.Value
            : health != null ? health.maxHP : 0;
        string side = battleManager.IsCharacterOnLocalPlayersTeam(character)
            ? "Takımın"
            : "Düşman";
        string owner = battleManager.IsCharacterControlledByLocalPlayer(character)
            ? " • Senin"
            : "";

        return
            $"{GetDisplayName(character)}  [{side}{owner}]\n" +
            $"HP {currentHp}/{maxHp}   STR {character.AttackNet.Value}   DEF {character.DefenseNet.Value}";
    }

    private static string GetDisplayName(Character character)
    {
        string value = character != null
            ? character.DisplayNameNet.Value.ToString()
            : string.Empty;
        return string.IsNullOrWhiteSpace(value)
            ? character != null ? character.name.Replace("_", " ") : "Karakter"
            : value.Replace("_", " ");
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        battleManager?.ShowLocalWarning(message);
    }

    private void EnsureRuntimeUI()
    {
        if (panelRect != null)
            return;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Canvas canvas = System.Array.Find(canvases, item =>
            item != null &&
            item.gameObject.scene == gameObject.scene &&
            item.renderMode != RenderMode.WorldSpace);

        if (canvas == null)
        {
            Debug.LogWarning("[ACTION UI] Ekran Canvas'ı bulunamadı.");
            return;
        }

        GameObject panel = new GameObject(
            "BattleCharacterSelectionPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        panel.transform.SetAsLastSibling();

        panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -82f);
        panelRect.sizeDelta = new Vector2(820f, 128f);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 0.90f);

        infoText = CreateText(
            panel.transform,
            "CharacterInfo",
            new Vector2(-195f, -12f),
            new Vector2(400f, 100f),
            22f);
        infoText.alignment = TextAlignmentOptions.Left;

        statusText = CreateText(
            panel.transform,
            "SelectionStatus",
            new Vector2(205f, -12f),
            new Vector2(380f, 100f),
            20f);
        statusText.alignment = TextAlignmentOptions.Center;

        if (attackButton == null)
        {
            attackButton = CreateButton(
                panel.transform,
                "SelectedAttackButton",
                "SALDIR",
                new Vector2(110f, -94f),
                new Vector2(150f, 44f));
        }

        if (skillButton == null)
        {
            skillButton = CreateButton(
                panel.transform,
                "SelectedSkillButton",
                "BECERİ",
                new Vector2(285f, -94f),
                new Vector2(160f, 44f));
        }

        panel.SetActive(false);
    }

    private static TMP_Text CreateText(
        Transform parent,
        string objectName,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize)
    {
        GameObject obj = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        GameObject obj = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = obj.GetComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.22f, 1f);

        Button button = obj.GetComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = CreateText(obj.transform, "Label", Vector2.zero, size, 22f);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.text = label;

        return button;
    }

    private static Camera GetCanvasCamera(RectTransform rect)
    {
        Canvas canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
    }
}