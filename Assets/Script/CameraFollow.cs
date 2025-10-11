using UnityEngine;
using Unity.Netcode;

public class CameraFollow : NetworkBehaviour

{
    public Transform target;
    public Vector3 offset = new Vector3(0, 2, -10);
    public float followSpeed = 5f;

    void LateUpdate()
    {
        if (target == null || !IsOwner) return;

        Vector3 desiredPosition = target.position + offset;
        Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, desiredPosition, followSpeed * Time.deltaTime);
    }
}
