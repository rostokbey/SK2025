using UnityEngine;

public class BattleCameraController : MonoBehaviour
{
    [Header("Camera")]
    public Camera mainCamera;

    [Header("Camera Points")]
    public Transform hostCameraPoint;
    public Transform clientCameraPoint;

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
            Debug.LogWarning("[Camera] Main Camera bulunamadı.");
            return;
        }

        bool isHostPlayer =
            Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClientId ==
            Unity.Netcode.NetworkManager.ServerClientId;

        Transform targetPoint =
            isHostPlayer ? hostCameraPoint : clientCameraPoint;

        if (targetPoint == null)
        {
            Debug.LogWarning("[Camera] Camera point atanmadı.");
            return;
        }

        mainCamera.transform.position = targetPoint.position;
        mainCamera.transform.rotation = targetPoint.rotation;

        Debug.Log(isHostPlayer
            ? "[Camera] Host kamera aktif."
            : "[Camera] Client kamera aktif.");
    }
}