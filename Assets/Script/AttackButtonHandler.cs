using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class AttackButtonHandler : MonoBehaviour
{
    public PlayerController localPlayer;

    public void OnAttackButtonClicked()
    {
        if (localPlayer != null)
            localPlayer.PerformAttackServerRpc();
    }
    void OnEnable()
    {
        if (NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
        }
    }
}

