using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class MyClientIdManager : NetworkBehaviour
{
    public Button attackButton;
    public Button skillButton;

    [SerializeField]
    private NetworkVariable<ulong> currentTurnClientId = new NetworkVariable<ulong>();

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager.Singleton.ConnectedClientsList.Count > 0)
        {
            currentTurnClientId.Value = NetworkManager.Singleton.ConnectedClientsList[0].ClientId;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // BattleScene'de tek doğruluk kaynağı BattleManager'dır. Eski iki
        // oyunculu sayaç seçim UI butonlarını tekrar açıp kapatmasın.
        if (BattleManager.Instance != null)
            return;

        bool isMyTurn = NetworkManager.Singleton.LocalClientId == currentTurnClientId.Value;
        if (attackButton != null) attackButton.interactable = isMyTurn;
        if (skillButton != null) skillButton.interactable = isMyTurn;
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTurnServerRpc()
    {
        StartNextTurn();
    }

    private void StartNextTurn()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        int playerCount = clients.Count;

        int currentIndex = -1;
        for (int i = 0; i < clients.Count; i++)
        {
            if (clients[i].ClientId == currentTurnClientId.Value)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex == -1) currentIndex = 0;

        int nextIndex = (currentIndex + 1) % playerCount;
        ulong nextClientId = clients[nextIndex].ClientId;

        currentTurnClientId.Value = nextClientId;

        var nextPlayer = NetworkManager.Singleton.ConnectedClients[nextClientId].PlayerObject;
        nextPlayer.GetComponent<PlayerInventory>()?.OnTurnStart();
    }
}
