using UnityEngine;
using Unity.Netcode;

public class BattleCameraView : MonoBehaviour
{
    [Header("Camera Positions")]
    public Transform hostCameraPoint;
    public Transform clientCameraPoint;

    [Header("Settings")]
    public float moveSpeed = 8f;
    public float rotateSpeed = 8f;

    private Transform targetPoint;

    void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("NetworkManager bulunamadı. Kamera Host açısına alındı.");
            targetPoint = hostCameraPoint;
            return;
        }

        if (NetworkManager.Singleton.IsHost)
        {
            targetPoint = hostCameraPoint;
            Debug.Log("Kamera Host açısına ayarlandı.");
        }
        else
        {
            targetPoint = clientCameraPoint;
            Debug.Log("Kamera Client/Join açısına ayarlandı.");
        }

        SnapToTarget();
    }

    void LateUpdate()
    {
        if (targetPoint == null) return;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPoint.position,
            moveSpeed * Time.deltaTime
        );

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetPoint.rotation,
            rotateSpeed * Time.deltaTime
        );
    }

    public void SnapToTarget()
    {
        if (targetPoint == null) return;

        transform.position = targetPoint.position;
        transform.rotation = targetPoint.rotation;
    }
}