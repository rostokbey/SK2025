using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

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

    // Tekil takım arkadaşı hedefi için butondan sonra dokunma beklenir.
    private bool awaitingAllyTarget;

    // Çoklu düşman yeteneklerinde seçim istemci tarafında hazırlanır;
    // sunucu target NetworkObject ID'lerini yeniden doğrular.
    private readonly List<Character> selectedMultiTargets = new List<Character>();
    private bool awaitingMultiTargetSelection;
    private int multiTargetRequired;
    private Button autoFillButton;
    private Button confirmMultiButton;
    private Button cancelMultiButton;
    private TMP_Text multiTargetCountText;

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
        if (!CanUseSelection(
                out string reason,
                requireEnemyTarget: selectedAttacker == null || !selectedAttacker.IsStunned))
        {
            SetStatus(reason);
            return false;
        }

        if (selectedAttacker.IsStunned)
        {
            selectedAttacker.AttackSelectedTargetServerRpc(
                selectedTarget != null ? selectedTarget.NetworkObjectId : 0UL);
            SetStatus("Sersemletilmiş karakter seçildi; aksiyon sunucuda boşa harcanacak.");
            return true;
        }

        if (!selectedAttacker.CanUseNormalAction)
        {
            SetStatus("Korku etkisi: normal saldırı kullanılamaz.");
            return false;
        }

        if (!IsFrontmostTargetInColumn(selectedTarget))
        {
            SetStatus("Bu hedef ön sıradaki karakter tarafından korunuyor.");
            return false;
        }

        selectedAttacker.AttackSelectedTargetServerRpc(
            selectedTarget.NetworkObjectId);
        SetStatus("Saldırı isteği sunucuya gönderildi.");
        return true;
    }

    public bool TryRequestActiveAbility()
    {
        if (!CanUseSelection(out string reason, requireEnemyTarget: false))
        {
            SetStatus(reason);
            return false;
        }

        if (selectedAttacker.IsStunned)
        {
            selectedAttacker.UseActiveAbilityTargetsServerRpc(System.Array.Empty<ulong>());
            SetStatus("Sersemletilmiş karakter seçildi; aksiyon sunucuda boşa harcanacak.");
            return true;
        }

        if (!selectedAttacker.IsActiveAbilityReady)
        {
            SetStatus(BattleManager.DescribeAbilityNotReady(selectedAttacker));
            return false;
        }

        IActiveAbility ability = AbilityCatalog.GetActive(selectedAttacker.cardData?.ability);
        if (ability == null)
        {
            SetStatus("Bu kartın aktif yeteneği kayıtlı değil.");
            return false;
        }

        V156TargetRule rule = ResolveTargetRule(ability);
        if (rule == V156TargetRule.MultiEnemy)
        {
            IV156TargetedAbility targetMetadata = ability as IV156TargetedAbility;
            multiTargetRequired = targetMetadata != null
                ? Mathf.Max(1, targetMetadata.RequiredTargets)
                : 1;
            selectedMultiTargets.Clear();
            awaitingMultiTargetSelection = true;
            awaitingAllyTarget = false;
            RefreshMultiTargetControls();
            SetStatus($"Ön hattan {multiTargetRequired} farklı düşman seç; Oto Doldur yardımcı olur.");
            return true;
        }

        if (rule == V156TargetRule.SingleAlly)
        {
            awaitingAllyTarget = true;
            awaitingMultiTargetSelection = false;
            SetStatus("Yeteneği uygulayacağın takım arkadaşına dokun.");
            return true;
        }

        if (rule == V156TargetRule.Self ||
            rule == V156TargetRule.AllEnemies ||
            rule == V156TargetRule.LowestHpAlly ||
            rule == V156TargetRule.AllAllies)
        {
            selectedAttacker.UseActiveAbilityTargetsServerRpc(System.Array.Empty<ulong>());
            SetStatus("Yetenek isteği sunucuya gönderildi.");
            return true;
        }

        if (!CanUseSelection(out reason, requireEnemyTarget: true))
        {
            SetStatus(reason);
            return false;
        }

        if (selectedTarget.IsInvisible)
        {
            SetStatus("Görünmez düşman tekil hedef seçilemez.");
            return false;
        }

        selectedAttacker.UseActiveAbilityTargetsServerRpc(
            new[] { selectedTarget.NetworkObjectId });
        SetStatus("Yetenek isteği sunucuya gönderildi.");
        return true;
    }

    private static V156TargetRule ResolveTargetRule(IActiveAbility ability)
    {
        if (ability is IV156TargetedAbility metadata)
            return metadata.TargetRule;

        if (ability is IAbilityTargetSide sided)
        {
            if (sided.TargetSide == AbilityTargetSide.Self)
                return V156TargetRule.Self;
            if (sided.TargetSide == AbilityTargetSide.Ally)
                return V156TargetRule.SingleAlly;
        }

        return V156TargetRule.SingleEnemy;
    }

    private bool CanUseSelection(out string reason, bool requireEnemyTarget = true)
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

        if (!requireEnemyTarget)
            return true;

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

    private bool IsLocalPlayersTurn()
    {
        return NetworkManager.Singleton != null &&
               battleManager != null &&
               battleManager.Phase.Value == BattlePhase.PlayerTurn &&
               battleManager.ActivePlayerClientId.Value == NetworkManager.Singleton.LocalClientId;
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

            if (awaitingMultiTargetSelection)
            {
                ToggleMultiTarget(character);
                return;
            }

            if (awaitingAllyTarget)
            {
                awaitingAllyTarget = false;

                if (selectedAttacker != null && character.IsAlive &&
                    battleManager.IsCharacterOnLocalPlayersTeam(character))
                {
                    selectedAttacker.UseActiveAbilityTargetsServerRpc(
                        new[] { character.NetworkObjectId });
                    SetStatus("Yetenek isteği sunucuya gönderildi.");
                }
                else
                {
                    SetStatus("Geçerli bir takım arkadaşı seçilmedi; yetenek iptal edildi.");
                }

                return;
            }

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

        if (awaitingAllyTarget)
        {
            awaitingAllyTarget = false;
            SetStatus("Yetenek iptal edildi.");
        }
    }

    private void ToggleMultiTarget(Character character)
    {
        if (selectedAttacker == null || character == null || !character.IsAlive)
        {
            SetStatus("Geçerli canlı hedef seç.");
            return;
        }

        if (character.isPlayer == selectedAttacker.isPlayer)
        {
            SetStatus("Çoklu düşman yeteneğinde takım arkadaşı seçilemez.");
            return;
        }

        if (character.gridRow != BattleRow.Front)
        {
            SetStatus("Çoklu hedef havuzu yalnızca düşman ön hattıdır.");
            return;
        }

        if (character.IsInvisible)
        {
            SetStatus("Görünmez düşman manuel hedef seçilemez.");
            return;
        }

        if (selectedMultiTargets.Contains(character))
        {
            selectedMultiTargets.Remove(character);
            SetStatus("Hedef seçimden çıkarıldı.");
            RefreshMultiTargetControls();
            return;
        }

        if (selectedMultiTargets.Count >= multiTargetRequired)
        {
            SetStatus($"En fazla {multiTargetRequired} farklı hedef seçebilirsin.");
            return;
        }

        selectedMultiTargets.Add(character);
        SetStatus($"Çoklu hedef: {selectedMultiTargets.Count}/{multiTargetRequired}.");
        RefreshMultiTargetControls();
    }

    private List<Character> GetSelectableFrontTargets()
    {
        var result = new List<Character>();
        if (selectedAttacker == null || battleManager == null)
            return result;

        BattleGrid grid = battleManager.GetGridFor(!selectedAttacker.isPlayer);
        if (grid == null)
            return result;

        foreach (Character character in grid.GetAllAlive())
            if (character.gridRow == BattleRow.Front && !character.IsInvisible)
                result.Add(character);

        return result;
    }

    private static int CurrentHp(Character character)
    {
        HealthSystem health = character != null ? character.GetComponent<HealthSystem>() : null;
        return health != null ? health.currentHP.Value : int.MaxValue;
    }

    private void AutoFillMultiTargets()
    {
        if (!awaitingMultiTargetSelection || selectedAttacker == null)
            return;

        List<Character> candidates = GetSelectableFrontTargets();
        candidates.Sort((a, b) =>
        {
            int hpOrder = CurrentHp(a).CompareTo(CurrentHp(b));
            if (hpOrder != 0) return hpOrder;
            int colOrder = a.gridColumn.CompareTo(b.gridColumn);
            return colOrder != 0 ? colOrder : ((int)a.gridRow).CompareTo((int)b.gridRow);
        });

        int targetLimit = Mathf.Min(multiTargetRequired, candidates.Count);
        foreach (Character candidate in candidates)
        {
            if (selectedMultiTargets.Count >= targetLimit)
                break;
            if (!selectedMultiTargets.Contains(candidate))
                selectedMultiTargets.Add(candidate);
        }

        SetStatus($"Oto Doldur: {selectedMultiTargets.Count}/{targetLimit} hedef seçildi.");
        RefreshMultiTargetControls();
    }

    private void ConfirmMultiTargets()
    {
        if (!awaitingMultiTargetSelection || selectedAttacker == null)
            return;

        List<Character> candidates = GetSelectableFrontTargets();
        int requiredNow = Mathf.Min(multiTargetRequired, candidates.Count);
        if (requiredNow <= 0)
        {
            SetStatus("Ön hatta seçilebilir canlı düşman yok.");
            return;
        }

        if (selectedMultiTargets.Count != requiredNow)
        {
            SetStatus($"{requiredNow} hedef seç veya Oto Doldur'u kullan.");
            return;
        }

        ulong[] targetIds = selectedMultiTargets
            .Where(target => target != null && target.IsAlive && !target.IsInvisible)
            .Select(target => target.NetworkObjectId)
            .ToArray();

        awaitingMultiTargetSelection = false;
        selectedAttacker.UseActiveAbilityTargetsServerRpc(targetIds);
        selectedMultiTargets.Clear();
        RefreshMultiTargetControls();
        SetStatus("Çoklu yetenek isteği sunucuya gönderildi.");
    }

    private void CancelMultiTargetSelection()
    {
        awaitingMultiTargetSelection = false;
        selectedMultiTargets.Clear();
        RefreshMultiTargetControls();
        SetStatus("Çoklu hedef seçimi iptal edildi.");
    }

    private void RefreshMultiTargetControls()
    {
        bool active = awaitingMultiTargetSelection;
        if (autoFillButton != null) autoFillButton.gameObject.SetActive(active);
        if (confirmMultiButton != null) confirmMultiButton.gameObject.SetActive(active);
        if (cancelMultiButton != null) cancelMultiButton.gameObject.SetActive(active);
        if (multiTargetCountText != null)
        {
            multiTargetCountText.gameObject.SetActive(active);
            if (active)
            {
                int possible = GetSelectableFrontTargets().Count;
                int needed = Mathf.Min(multiTargetRequired, possible);
                multiTargetCountText.text = $"Hedef {selectedMultiTargets.Count}/{needed}";
            }
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
            if (other == null || other == target || !other.IsAlive || other.IsInvisible)
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
        if ((awaitingAllyTarget || awaitingMultiTargetSelection) &&
            (selectedAttacker == null || !IsLocalPlayersTurn()))
        {
            awaitingAllyTarget = false;
            awaitingMultiTargetSelection = false;
            selectedMultiTargets.Clear();
            RefreshMultiTargetControls();
        }

        selectedMultiTargets.RemoveAll(target =>
            target == null || !target.IsAlive || target.IsInvisible ||
            selectedAttacker == null || target.isPlayer == selectedAttacker.isPlayer ||
            target.gridRow != BattleRow.Front);

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
        bool hasAttacker =
            IsLocalPlayersTurn() &&
            selectedAttacker != null &&
            selectedAttacker.IsAlive;

        bool hasEnemyTarget =
            selectedTarget != null &&
            selectedTarget.IsAlive;

        if (attackButton != null)
            attackButton.interactable = hasAttacker &&
                (hasEnemyTarget || selectedAttacker.IsStunned);

        // Buton, sıra sendeyken ve kendi karakterin seçiliyken HER ZAMAN basılabilir kalır.
        // Pasif (gri) bir buton hiç tepki vermediği için oyuncu nedenini göremiyordu;
        // şimdi basınca "hazır değil / hedef seç / sersemletildi" gibi neden gösteriliyor.
        if (skillButton != null)
            skillButton.interactable = hasAttacker;

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

        string abilityLine = string.Empty;
        if (battleManager.IsCharacterControlledByLocalPlayer(character) && character.IsAlive)
        {
            abilityLine = character.IsActiveAbilityReady
                ? "\nYetenek: HAZIR"
                : "\n" + BattleManager.DescribeAbilityNotReady(character);
        }

        return
            $"{GetDisplayName(character)}  [{side}{owner}]\n" +
            $"HP {currentHp}/{maxHp}   STR {character.AttackNet.Value}   DEF {character.DefenseNet.Value}" +
            abilityLine;
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

        Canvas canvas = BattleCanvasUtility.FindScreenCanvas(gameObject.scene);

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
        panelRect.sizeDelta = new Vector2(1040f, 205f);
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

        multiTargetCountText = CreateText(
            panel.transform,
            "MultiTargetCount",
            new Vector2(-425f, -151f),
            new Vector2(150f, 38f),
            18f);
        multiTargetCountText.alignment = TextAlignmentOptions.Center;
        autoFillButton = CreateButton(
            panel.transform,
            "MultiTargetAutoFill",
            "OTO DOLDUR",
            new Vector2(-225f, -151f),
            new Vector2(175f, 42f));
        confirmMultiButton = CreateButton(
            panel.transform,
            "MultiTargetConfirm",
            "ONAYLA",
            new Vector2(-15f, -151f),
            new Vector2(150f, 42f));
        cancelMultiButton = CreateButton(
            panel.transform,
            "MultiTargetCancel",
            "İPTAL",
            new Vector2(175f, -151f),
            new Vector2(140f, 42f));

        autoFillButton.onClick.AddListener(AutoFillMultiTargets);
        confirmMultiButton.onClick.AddListener(ConfirmMultiTargets);
        cancelMultiButton.onClick.AddListener(CancelMultiTargetSelection);
        RefreshMultiTargetControls();

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
