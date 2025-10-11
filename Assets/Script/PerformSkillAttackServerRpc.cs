using UnityEngine;
using Unity.Netcode;

public class PerformSkillAttackServerRpc : NetworkBehaviour
{
    [ServerRpc]
    public void SendAttackServerRpc()
    {
        Debug.Log("Saldýrý server'a gönderildi.");
    }
}
