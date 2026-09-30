using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Her ağ karakterinin üstünde görünen hafif dünya-uzayı can barı.
/// Prefaba elle eklenmesi gerekmez; Character.OnNetworkSpawn istemcide
/// otomatik ekler. HP kaynağı HealthSystem.currentHP NetworkVariable'ıdır.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterWorldHealthBar : MonoBehaviour
{
    private const float CanvasScale = 0.01f;
    private const float BarWidth = 100f;
    private const float BarHeight = 11f;

    private Character character;
    private HealthSystem health;
    private Renderer[] modelRenderers;
    private Transform barRoot;
    private RectTransform fillRect;
    private Image fillImage;
    private Camera cachedCamera;

    private void Awake()
    {
        character = GetComponent<Character>();
        health = GetComponent<HealthSystem>();
        modelRenderers = GetComponentsInChildren<Renderer>(true);
        BuildBar();
        BuildClickTarget();
    }

    private void LateUpdate()
    {
        if (barRoot == null || health == null)
            return;

        int maximum = character != null && character.MaxHpNet.Value > 0
            ? character.MaxHpNet.Value
            : Mathf.Max(1, health.maxHP);
        float normalized = Mathf.Clamp01((float)health.currentHP.Value / maximum);

        if (!barRoot.gameObject.activeSelf)
            barRoot.gameObject.SetActive(true);

        if (fillRect != null)
            fillRect.anchorMax = new Vector2(normalized, 1f);

        if (fillImage != null)
        {
            fillImage.color = normalized > 0.55f
                ? new Color(0.18f, 0.90f, 0.28f, 1f)
                : normalized > 0.25f
                    ? new Color(1f, 0.72f, 0.10f, 1f)
                    : new Color(0.95f, 0.15f, 0.12f, 1f);
        }

        barRoot.position = ResolveWorldPosition();

        if (cachedCamera == null)
            cachedCamera = Camera.main;

        if (cachedCamera != null)
        {
            Vector3 awayFromCamera =
                barRoot.position - cachedCamera.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.0001f)
                barRoot.rotation = Quaternion.LookRotation(
                    awayFromCamera.normalized,
                    Vector3.up);
        }
    }

    private Vector3 ResolveWorldPosition()
    {
        if (TryGetModelBounds(out Bounds combined))
        {
            float offset = Mathf.Clamp(combined.size.y * 0.08f, 0.12f, 0.45f);
            return new Vector3(
                combined.center.x,
                combined.max.y + offset,
                combined.center.z);
        }

        return transform.position + Vector3.up * 2.2f;
    }

    private bool TryGetModelBounds(out Bounds combined)
    {
        bool hasBounds = false;
        combined = default;

        if (modelRenderers == null)
            return false;

        foreach (Renderer modelRenderer in modelRenderers)
        {
            if (modelRenderer == null || !modelRenderer.enabled)
                continue;

            if (!hasBounds)
            {
                combined = modelRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(modelRenderer.bounds);
            }
        }

        return hasBounds;
    }

    private void BuildClickTarget()
    {
        if (transform.Find("CharacterClickTarget") != null ||
            !TryGetModelBounds(out Bounds bounds))
        {
            return;
        }

        GameObject clickTarget = new GameObject(
            "CharacterClickTarget",
            typeof(BoxCollider));
        clickTarget.layer = gameObject.layer;
        clickTarget.transform.SetParent(transform, false);
        clickTarget.transform.position = bounds.center;
        clickTarget.transform.rotation = transform.rotation;

        Vector3 scale = transform.lossyScale;
        Vector3 safeScale = new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

        BoxCollider box = clickTarget.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(
            bounds.size.x / safeScale.x,
            bounds.size.y / safeScale.y,
            bounds.size.z / safeScale.z);
    }

    private void BuildBar()
    {
        GameObject root = new GameObject(
            "CharacterHealthBar",
            typeof(RectTransform),
            typeof(Canvas));
        root.transform.SetParent(transform, false);
        barRoot = root.transform;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        RectTransform canvasRect = root.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        canvasRect.localScale = Vector3.one * CanvasScale;

        GameObject backgroundObject = CreateImage(
            "Background",
            canvasRect,
            new Color(0.04f, 0.04f, 0.04f, 0.88f));
        RectTransform backgroundRect =
            backgroundObject.GetComponent<RectTransform>();
        Stretch(backgroundRect, 0f);

        GameObject fillObject = CreateImage(
            "Fill",
            backgroundRect,
            new Color(0.18f, 0.90f, 0.28f, 1f));
        fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        fillImage = fillObject.GetComponent<Image>();
    }

    private static GameObject CreateImage(
        string objectName,
        Transform parent,
        Color color)
    {
        GameObject result = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        result.transform.SetParent(parent, false);

        Image image = result.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return result;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}