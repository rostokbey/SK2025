using UnityEngine;

public class NetworkManagerController : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(this.gameObject); // Bu GameObject sahne geçişlerinde silinmesin
    }
}
