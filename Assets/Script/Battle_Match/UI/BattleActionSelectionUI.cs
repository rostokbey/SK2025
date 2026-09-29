using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1v1 savaş için yerel karakter ve hedef sütunu seçimi.
/// Seçim istemcide yapılır; saldırı/yetenek isteği mevcut sunucu-otoriteli
/// Character RPC'lerine gönderilir ve sunucuda yeniden doğrulanır.
/// </summary>
public sealed class BattleActionSelectionUI : MonoBehaviour
{
    private BattleManager battleManager;
    private Character selectedCharacter;
    private int selectedTargetColumn;

    private RectTransform panelRect;
    private TMP_Text statusText;
    private Button attackButton;
    private Button skillButton;
    private readonly List<Button> columnButtons = new();

    public void Initialize(BattleManager manager)
    {
        battleManager = manager;
        selectedTargetColumn = 0;
        EnsureRuntimeUI();
        RefreshUI();
    }

    private void Update()
    {
        if (battleManager == null)
            return;

        bool combatVisible = battleManager.Phase.Value == BattlePhase.PlayerTurn;
        if (panelRect != null && panelRect.gameObject.activeSelf != combatVisible)
            panelRect.gameObject.SetActive(combatVisible);

        if (!combatVisible)
            return;

        TrySelectCharacterFromPointer();
        ValidateSelection();
        RefreshUI();
    }

    public bool TryRequestAttack()
    {
        if (!CanUseSelectedCharacter(out string reason))
        {
            SetStatus(reason);
            return false;
        }

        selectedCharacter.AttackServerRpc(selectedTargetColumn);
        return true;
    }

    public bool TryRequestActiveAbility()
    {
        if (!CanUseSelectedCharacter(out string reason))
        {
            SetStatus(reason);
            return false;
        }

        if (!selectedCharacter.IsActiveAbilityReady)
        {
            SetStatus($"Yetenek hazır değil: {selectedCharacter.AbilityCooldownRemaining.Value} raund kaldı.");
            return false;
        }

        selectedCharacter.UseActiveAbilityServerRpc(selectedTargetColumn);
        return true;
    }

    private bool CanUseSelectedCharacter(out string reason)
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

        if (selectedCharacter == null)
        {
            reason = "Önce sahadaki canlı karakterlerinden birine dokun.";
            return false;
        }

        if (!selectedCharacter.IsAlive)
        {
            reason = "Seçili karakter düşmüş durumda.";
            return false;
        }

        if (!IsLocalPlayersCharacter(selectedCharacter))
        {
            reason = "Yalnızca kendi karakterini seçebilirsin.";
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

        if (panelRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(
                panelRect,
                Input.mousePosition,
                GetCanvasCamera(panelRect)))
        {
            return;
        }

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

            if (!character.IsAlive || !IsLocalPlayersCharacter(character))
            {
                SetStatus("Yalnızca kendi canlı karakterlerinden birini seçebilirsin.");
                return;
            }

            selectedCharacter = character;
            if (statusText != null)
                statusText.text = $"Karakter seçildi. Hedef sütun: {selectedTargetColumn + 1}";
            return;
        }
    }

    private bool IsLocalPlayersCharacter(Character character)
    {
        if (character == null || NetworkManager.Singleton == null)
            return false;

        bool localIsHostSide =
            NetworkManager.Singleton.LocalClientId == NetworkManager.ServerClientId;
        return character.isPlayer == localIsHostSide;
    }

    private void ValidateSelection()
    {
        if (selectedCharacter != null &&
            (!selectedCharacter.IsAlive || !IsLocalPlayersCharacter(selectedCharacter)))
        {
            selectedCharacter = null;
        }
    }

    private void SelectTargetColumn(int column)
    {
        selectedTargetColumn = Mathf.Clamp(column, 0, BattleRules.OneVsOneColumns - 1);
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (statusText == null)
            return;

        bool isMyTurn =
            NetworkManager.Singleton != null &&
            battleManager != null &&
            battleManager.Phase.Value == BattlePhase.PlayerTurn &&
            battleManager.ActivePlayerClientId.Value == NetworkManager.Singleton.LocalClientId;

        bool hasUsableSelection =
            selectedCharacter != null &&
            selectedCharacter.IsAlive &&
            IsLocalPlayersCharacter(selectedCharacter);

        if (attackButton != null)
            attackButton.interactable = isMyTurn && hasUsableSelection;

        if (skillButton != null)
            skillButton.interactable =
                isMyTurn &&
                hasUsableSelection &&
                selectedCharacter.IsActiveAbilityReady;

        for (int i = 0; i < columnButtons.Count; i++)
        {
            ColorBlock colors = columnButtons[i].colors;
            colors.normalColor = i == selectedTargetColumn
                ? new Color(0.20f, 0.65f, 1f, 1f)
                : new Color(0.18f, 0.18f, 0.22f, 1f);
            columnButtons[i].colors = colors;
        }

        if (selectedCharacter == null)
        {
            statusText.text = isMyTurn
                ? "Karakterine dokun, hedef sütunu seç, sonra Saldır veya Yetenek kullan."
                : "Rakibin hamlesi bekleniyor.";
            return;
        }

        string cooldown = selectedCharacter.IsActiveAbilityReady
            ? "Hazır"
            : $"{selectedCharacter.AbilityCooldownRemaining.Value} raund";
        statusText.text =
            $"Seçili #{selectedCharacter.NetworkObjectId} • Hedef sütun {selectedTargetColumn + 1} • Yetenek: {cooldown}";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        if (battleManager != null)
            battleManager.ShowLocalWarning(message);
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
            "BattleActionSelectionPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        panel.transform.SetAsLastSibling();

        panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -92f);
        panelRect.sizeDelta = new Vector2(920f, 142f);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 0.88f);

        statusText = CreateText(panel.transform, "ActionStatus", new Vector2(0f, -12f), new Vector2(880f, 42f), 24f);
        statusText.alignment = TextAlignmentOptions.Center;

        const float firstColumnX = -300f;
        for (int i = 0; i < BattleRules.OneVsOneColumns; i++)
        {
            int column = i;
            Button button = CreateButton(
                panel.transform,
                $"TargetColumn{i + 1}",
                (i + 1).ToString(),
                new Vector2(firstColumnX + i * 66f, -82f),
                new Vector2(56f, 50f));
            button.onClick.AddListener(() => SelectTargetColumn(column));
            columnButtons.Add(button);
        }

        attackButton = CreateButton(
            panel.transform,
            "SelectedAttackButton",
            "SALDIR",
            new Vector2(175f, -82f),
            new Vector2(150f, 50f));
        attackButton.onClick.AddListener(() => TryRequestAttack());

        skillButton = CreateButton(
            panel.transform,
            "SelectedSkillButton",
            "YETENEK",
            new Vector2(350f, -82f),
            new Vector2(170f, 50f));
        skillButton.onClick.AddListener(() => TryRequestActiveAbility());
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