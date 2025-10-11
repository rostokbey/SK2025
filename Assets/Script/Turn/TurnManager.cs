using System.Linq;
using UnityEngine;
using Unity.Netcode;

public class TurnManager : NetworkBehaviour
{
    public static TurnManager Instance;

    private NetworkVariable<ulong> currentTurnPlayer = new();

    void Awake() => Instance = this;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentTurnPlayer.Value = NetworkManager.Singleton.ConnectedClientsIds[0];
        }
    }

    public bool IsMyTurn(ulong playerId)
    {
        return currentTurnPlayer.Value == playerId;
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        var allPlayers = NetworkManager.Singleton.ConnectedClientsIds.ToList();
        int index = allPlayers.IndexOf(currentTurnPlayer.Value);
        currentTurnPlayer.Value = allPlayers[(index + 1) % allPlayers.Count];

        Debug.Log($"Sıra artık Player {currentTurnPlayer.Value}'da.");

        // 🎯 Yeni oyuncuya tur başladı bilgisini gönder
        SendTurnStartedClientRpc(currentTurnPlayer.Value);
    }

    [ClientRpc]
    private void SendTurnStartedClientRpc(ulong currentPlayerId)
    {
        if (NetworkManager.Singleton.LocalClientId == currentPlayerId)
        {
            // ✅ Artık TurnDrawManager yerine HandUIManager kullanıyoruz
            var handUI = FindObjectOfType<HandUIManager>();
            if (handUI != null)
            {
                handUI.DrawCard(); // yeni tur başladığında otomatik kart çek
            }
        }
    }
}
