using UnityEngine;

public class GeciciYolBulucu : MonoBehaviour
{
    void Start()
    {
        // Klasör yolunu Console ekranına yazdırır
        Debug.Log("Persistent Data Path: " + Application.persistentDataPath);
    }
}