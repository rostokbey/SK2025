using UnityEngine;
using Unity.Netcode;

public class BattleCameraView : MonoBehaviour
{
    public Transform hostCameraPoint;
    public Transform clientCameraPoint;

    void Start()
    {
        Transform targetPoint = hostCameraPoint;

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsHost)
            targetPoint = clientCameraPoint;

        if (targetPoint == null) return;

        transform.position = targetPoint.position;
        transform.rotation = targetPoint.rotation;
    }
}