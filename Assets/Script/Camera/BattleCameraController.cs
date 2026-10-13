using UnityEngine;
using UnityEngine.EventSystems;

public class BattleCameraController : MonoBehaviour
{
    [Header("Camera")]
    public Camera mainCamera;

    [Header("Camera Points")]
    public Transform hostCameraPoint;
    public Transform clientCameraPoint;

    [Header("Orbit Hedefi")]
    [Tooltip("Savas alaninin ortasina koydugun bos objeyi surukle. Bos birakilirsa kameranin baktigi zemin noktasi kullanilir.")]
    public Transform orbitTarget;

    [Header("Limitler")]
    public float fallbackTargetDistance = 100f;
    public float minDistance = 10f;
    public float maxDistance = 600f;
    public float yawLimit = 30f;
    public float pitchLimit = 25f;

    [Header("Tek Parmak Kontrol")]
    public bool oneFingerRotate = true;
    public float touchRotateSpeed = 0.08f;
    [Tooltip("Bu kadar pikselden az hareket dokunma (secim) sayilir, kamera kipirdamaz.")]
    public float dragThresholdPixels = 12f;

    [Header("Iki Parmak (Yedek)")]
    public float pinchZoomSpeed = 0.15f;

    [Header("Zoom Butonlari")]
    public bool showZoomButtons = true;
    public float zoomButtonSpeed = 60f;

    [Header("PC Test Kontrol")]
    public float mouseRotateSpeed = 120f;
    public float mouseZoomSpeed = 15f;

    private bool isInitialized;
    private Vector3 pivot;
    private float distance, yaw, pitch;
    private float baseYaw, basePitch, baseDistance;

    private int dragFingerId = -1;
    private Vector2 dragStartPos;
    private bool dragStarted;

    private GUIStyle zoomButtonStyle;

    private Rect ZoomInRect => new Rect(Screen.width - 150f, Screen.height - 230f, 120f, 90f);
    private Rect ZoomOutRect => new Rect(Screen.width - 150f, Screen.height - 130f, 120f, 90f);

    private void Start()
    {
        SetupCamera();
    }

    private void SetupCamera()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogWarning("[Camera] Main Camera bulunamadi.");
            return;
        }

        bool isHostPlayer =
            Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClientId ==
            Unity.Netcode.NetworkManager.ServerClientId;

        Transform targetPoint = isHostPlayer ? hostCameraPoint : clientCameraPoint;

        if (targetPoint == null)
        {
            Debug.LogWarning("[Camera] Camera point atanmadi.");
            return;
        }

        mainCamera.transform.SetPositionAndRotation(
            targetPoint.position,
            targetPoint.rotation);

        Debug.Log(isHostPlayer
            ? "[Camera] Host kamera aktif."
            : "[Camera] Client kamera aktif.");

        InitializeOrbit();
    }

    private void InitializeOrbit()
    {
        // 1) OrbitTarget atanmissa onu kullan (en saglam yontem)
        if (orbitTarget != null)
        {
            pivot = orbitTarget.position;
        }
        else
        {
            // 2) Kameranin baktigi yonde zemine raycast at
            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 3000f))
                pivot = hit.point;
            else
                // 3) O da yoksa bakis yonunde sabit mesafe
                pivot = mainCamera.transform.position +
                        mainCamera.transform.forward * fallbackTargetDistance;
        }

        Vector3 offset = mainCamera.transform.position - pivot;

        distance = Mathf.Max(1f, offset.magnitude);
        yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;

        baseYaw = yaw;
        basePitch = pitch;      // Acilis acisi neyse o korunur
        baseDistance = distance;

        isInitialized = true;

        // DIKKAT: Burada ApplyOrbit CAGIRILMIYOR.
        // Acilis goruntusu Camera Point ile birebir ayni kalir.
        Debug.Log($"[Camera] Orbit hazir -> Mesafe: {distance:F1}, Yaw: {yaw:F1}, Pitch: {pitch:F1}");

        ApplyOrbit();
    }

    private void Update()
    {
        if (!isInitialized || mainCamera == null)
            return;

        bool changed = Input.touchCount > 0
            ? HandleTouchControls()
            : HandleMouseControls();

        if (changed)
            ApplyOrbit();
    }

    // ───────── TEK PARMAK + PINCH ─────────

    private bool HandleTouchControls()
    {
        // Uc parmak dokunma: kamerayi sifirla
        if (Input.touchCount >= 3)
        {
            foreach (Touch t in Input.touches)
            {
                if (t.phase == TouchPhase.Began)
                {
                    ResetCamera();
                    break;
                }
            }
            EndDrag();
            return false;
        }

        // Iki parmak: pinch zoom (yedek)
        if (Input.touchCount == 2)
        {
            EndDrag();

            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            if (IsTouchBlocked(t0) || IsTouchBlocked(t1))
                return false;

            Vector2 prev0 = t0.position - t0.deltaPosition;
            Vector2 prev1 = t1.position - t1.deltaPosition;

            float pinchDelta =
                Vector2.Distance(t0.position, t1.position) -
                Vector2.Distance(prev0, prev1);

            if (Mathf.Abs(pinchDelta) > 0.01f)
            {
                distance -= pinchDelta * pinchZoomSpeed;
                return true;
            }
            return false;
        }

        // Tek parmak: yatay = saga/sola, dikey = yukari/asagi
        if (Input.touchCount == 1 && oneFingerRotate)
        {
            Touch t = Input.GetTouch(0);

            if (t.phase == TouchPhase.Began)
            {
                if (IsTouchBlocked(t))
                {
                    dragFingerId = -1;
                    return false;
                }

                dragFingerId = t.fingerId;
                dragStartPos = t.position;
                dragStarted = false;
                return false;
            }

            if (t.fingerId != dragFingerId)
                return false;

            // Kucuk hareketler secim (tap) sayilsin, kamera kipirdamasin
            if (!dragStarted)
            {
                if (Vector2.Distance(t.position, dragStartPos) < dragThresholdPixels)
                    return false;

                dragStarted = true;
            }

            if (t.phase == TouchPhase.Moved)
            {
                yaw += t.deltaPosition.x * touchRotateSpeed;
                pitch -= t.deltaPosition.y * touchRotateSpeed;
                return true;
            }

            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                EndDrag();
        }

        return false;
    }

    private void EndDrag()
    {
        dragFingerId = -1;
        dragStarted = false;
    }

    // UI butonu veya ekrandaki zoom butonu ustundeyse kamerayi devreye sokma
    private bool IsTouchBlocked(Touch t)
    {
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject(t.fingerId))
            return true;

        if (showZoomButtons)
        {
            // GUI koordinati yukaridan asagi, touch asagidan yukari -> Y cevir
            Vector2 guiPos = new Vector2(t.position.x, Screen.height - t.position.y);
            if (ZoomInRect.Contains(guiPos) || ZoomOutRect.Contains(guiPos))
                return true;
        }

        return false;
    }

    // ───────── PC TESTI ─────────

    private bool HandleMouseControls()
    {
        bool changed = false;

        if (Input.GetMouseButton(1))
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");

            if (Mathf.Abs(mx) > 0.001f || Mathf.Abs(my) > 0.001f)
            {
                yaw += mx * mouseRotateSpeed * Time.deltaTime;
                pitch -= my * mouseRotateSpeed * Time.deltaTime;
                changed = true;
            }
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            distance -= scroll * mouseZoomSpeed;
            changed = true;
        }

        if (Input.GetKeyDown(KeyCode.R))
            ResetCamera();

        return changed;
    }

    // ───────── ZOOM BUTONLARI (EKRAN) ─────────

    private void OnGUI()
    {
        if (!showZoomButtons || !isInitialized)
            return;

        if (zoomButtonStyle == null)
            zoomButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 40 };

        bool dirty = false;

        if (GUI.RepeatButton(ZoomInRect, "+", zoomButtonStyle))
        {
            distance -= zoomButtonSpeed * Time.unscaledDeltaTime;
            dirty = true;
        }

        if (GUI.RepeatButton(ZoomOutRect, "-", zoomButtonStyle))
        {
            distance += zoomButtonSpeed * Time.unscaledDeltaTime;
            dirty = true;
        }

        if (dirty)
            ApplyOrbit();
    }

    // ───────── ORBIT ─────────

    private void ApplyOrbit()
    {
        float yawOffset = Mathf.Clamp(
            Mathf.DeltaAngle(baseYaw, yaw), -yawLimit, yawLimit);
        yaw = baseYaw + yawOffset;

        // Acilis acisinin etrafinda +-pitchLimit, boylece acilista asla snap olmaz
        pitch = Mathf.Clamp(pitch,
            Mathf.Max(2f, basePitch - pitchLimit),
            Mathf.Min(88f, basePitch + pitchLimit));

        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        mainCamera.transform.position =
            pivot + rot * new Vector3(0f, 0f, -distance);
        mainCamera.transform.LookAt(pivot);
    }

    public void ResetCamera()
    {
        if (!isInitialized)
            return;

        yaw = baseYaw;
        pitch = basePitch;
        distance = baseDistance;
        ApplyOrbit();
    }
}