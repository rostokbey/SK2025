using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class SkillButtonHandler : MonoBehaviour
{
    public PlayerController localPlayer;

    public void OnSkillButtonClicked()
    {
        if (localPlayer != null)
        {
            localPlayer.PerformSkillAttackServerRpc();
        }
        else
        {
            Debug.LogWarning("LocalPlayer atanmamýþ!");
        }
    }
    void OnEnable()
    {
        StartCoroutine(AssignLocalPlayer());
    }

    System.Collections.IEnumerator AssignLocalPlayer()
    {
        while (localPlayer == null)
        {
            if (NetworkManager.Singleton.LocalClient != null &&
                NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            }
            yield return null; // bir sonraki frame'e kadar bekle
        }
    }

}
