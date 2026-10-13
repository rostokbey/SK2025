using UnityEngine;
using UnityEngine.EventSystems;

public class BattleCameraController : MonoBehaviour
{
    [Header("Camera")]
    public Camera mainCamera;

    [Header("Camera Points")]
    public Transform hostCameraPoint;
    public Transform clientCameraPoint;

    [Header("Orbit Target")]
    [Tooltip("Bos birakilirsa kameranin baktigi nokta otomatik merkez alinir.")]
    public Transform orbitTarget;

    public float autoTargetDistance = 35f;
    public float minDistance = 15f;
    public float maxDistance = 180f;
    public float minPitch = 5f;
    public float maxPitch = 75f;
    public float yawLimit = 30f;

    [Header("Mobile Controls")]
    public float touchRotateSpeed = 0.08f;
    public float pinchZoomSpeed = 0.1f;

    [Tooltip("Acilirsa tek parmak surukleme kamerayi dondurur. Kart/karakter secimiyle cakisabilir.")]
    public bool allowOneFingerDrag = false;

    [Header("Editor Mouse Controls")]
    public float mouseRotateSpeed = 120f;
    public float mouseZoomSpeed = 10f;

    private bool isInitialized;
    private Vector3 pivot;

    private float distance;
    private float yaw;
    private float pitch;

    private float baseYaw;
    private float basePitch;
    private float baseDistance;

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
        pivot = orbitTarget != null
            ? orbitTarget.position
            : mainCamera.transform.position +
              mainCamera.transform.forward * autoTargetDistance;

        Vector3 offset = mainCamera.transform.position - pivot;

        distance = Mathf.Max(1f, offset.magnitude);
        yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(
            Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;

        baseYaw = yaw;
        basePitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        pitch = basePitch;

        baseDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        distance = baseDistance;

        isInitialized = true;
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

    private bool HandleTouchControls()
    {
        // Uc parmakla dokunma: kamerayi ilk host/client gorunusune sifirlar.
        if (Input.touchCount == 3)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).phase == TouchPhase.Began)
                    ResetCamera();
            }

            return false;
        }

        // Iki parmak: yatay surukleme dondurur, pinch yakinlastirir/uzaklastirir.
        if (Input.touchCount == 2)
        {
            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            if (IsTouchOverUI(touch0) || IsTouchOverUI(touch1))
                return false;

            Vector2 previous0 = touch0.position - touch0.deltaPosition;
            Vector2 previous1 = touch1.position - touch1.deltaPosition;

            float previousPinchDistance = Vector2.Distance(previous0, previous1);
            float currentPinchDistance = Vector2.Distance(touch0.position, touch1.position);

            distance -= (currentPinchDistance - previousPinchDistance) *
                        pinchZoomSpeed;

            float averageDeltaX =
                (touch0.deltaPosition.x + touch1.deltaPosition.x) * 0.5f;

            float averageDeltaY =
                (touch0.deltaPosition.y + touch1.deltaPosition.y) * 0.5f;

            yaw += averageDeltaX * touchRotateSpeed;
            pitch -= averageDeltaY * touchRotateSpeed;

            return true;
        }

        // Tek parmak normalde kart/karakter secimi icin serbest birakilir.
        // Inspector'dan allowOneFingerDrag acilirsa tek parmak da dondurur.
        if (Input.touchCount == 1 && allowOneFingerDrag)
        {
            Touch touch = Input.GetTouch(0);

            if (IsTouchOverUI(touch))
                return false;

            if (touch.phase == TouchPhase.Moved)
            {
                yaw += touch.deltaPosition.x * touchRotateSpeed;
                pitch -= touch.deltaPosition.y * touchRotateSpeed;
                return true;
            }
        }

        return false;
    }

    private bool HandleMouseControls()
    {
        bool changed = false;

        // Unity Editor / PC testi: sag mouse basili surukleme.
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            if (Mathf.Abs(mouseX) > 0.001f || Mathf.Abs(mouseY) > 0.001f)
            {
                yaw += mouseX * mouseRotateSpeed * Time.deltaTime;
                pitch -= mouseY * mouseRotateSpeed * Time.deltaTime;
                changed = true;
            }
        }

        // Unity Editor / PC testi: mouse tekerlegi.
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            distance -= scroll * mouseZoomSpeed;
            changed = true;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetCamera();
        }

        return changed;
    }

    private void ApplyOrbit()
    {
        float yawOffset = Mathf.Clamp(
            Mathf.DeltaAngle(baseYaw, yaw),
            -yawLimit,
            yawLimit);

        yaw = baseYaw + yawOffset;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        mainCamera.transform.position =
            pivot + rotation * new Vector3(0f, 0f, -distance);

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

    private bool IsTouchOverUI(Touch touch)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(touch.fingerId);
    }
}