using UnityEngine;

public class ModalGuard : MonoBehaviour
{
    private SceneUIController sceneController;

    void Awake()
    {
        sceneController = FindObjectOfType<SceneUIController>();
    }

    void OnEnable()
    {
        if (sceneController != null)
        {
            sceneController.SetModal(true);
        }
    }

    void OnDisable()
    {
        if (sceneController != null)
        {
            sceneController.SetModal(false);
        }
    }
}