using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class LobbyTeamManager : NetworkBehaviour
{
    public static LobbyTeamManager Instance;

    [Header("Team Settings")]
    [SerializeField] private int maxPlayersPerTeam = 1;

    [Header("Team UI")]
    public Transform ayContent;
    public Transform kanContent;
    public GameObject playerRowPrefab;

    [Header("Buttons")]
    public Button switchTeamButton;

    private readonly List<string> ayPlayers = new();
    private readonly List<string> kanPlayers = new();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (switchTeamButton != null)
            switchTeamButton.onClick.AddListener(SwitchLocalPlayerTeam);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsClient)
            SubmitPlayerToLobbyServerRpc(GetTemporaryLocalName());
    }

    public void SetMaxPlayersPerTeam(int value)
    {
        maxPlayersPerTeam = Mathf.Max(1, value);

        if (IsServer)
            SyncTeamsClientRpc(string.Join("|", ayPlayers), string.Join("|", kanPlayers), maxPlayersPerTeam);
        else if (IsClient)
            SetMaxPlayersPerTeamServerRpc(maxPlayersPerTeam);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetMaxPlayersPerTeamServerRpc(int value)
    {
        maxPlayersPerTeam = Mathf.Max(1, value);
        SyncTeamsClientRpc(string.Join("|", ayPlayers), string.Join("|", kanPlayers), maxPlayersPerTeam);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitPlayerToLobbyServerRpc(string playerName, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        string displayName = $"{playerName} ({clientId})";

        if (ayPlayers.Contains(displayName) || kanPlayers.Contains(displayName))
            return;

        if (ayPlayers.Count <= kanPlayers.Count && ayPlayers.Count < maxPlayersPerTeam)
            ayPlayers.Add(displayName);
        else if (kanPlayers.Count < maxPlayersPerTeam)
            kanPlayers.Add(displayName);
        else
            Debug.LogWarning("[LobbyTeam] Takımlar dolu.");

        SyncTeamsClientRpc(string.Join("|", ayPlayers), string.Join("|", kanPlayers), maxPlayersPerTeam);
    }

    public void SwitchLocalPlayerTeam()
    {
        if (!IsClient) return;

        SwitchTeamServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SwitchTeamServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        string suffix = $"({clientId})";

        string foundPlayer = null;

        foreach (string player in ayPlayers)
        {
            if (player.EndsWith(suffix))
            {
                foundPlayer = player;
                break;
            }
        }

        if (foundPlayer != null)
        {
            if (kanPlayers.Count >= maxPlayersPerTeam)
            {
                SyncTeamsClientRpc(string.Join("|", ayPlayers), string.Join("|", kanPlayers), maxPlayersPerTeam);
                return;
            }

            ayPlayers.Remove(foundPlayer);
            kanPlayers.Add(foundPlayer);
        }
        else
        {
            foreach (string player in kanPlayers)
            {
                if (player.EndsWith(suffix))
                {
                    foundPlayer = player;
                    break;
                }
            }

            if (foundPlayer != null)
            {
                if (ayPlayers.Count >= maxPlayersPerTeam)
                {
                    SyncTeamsClientRpc(string.Join("|", ayPlayers), string.Join("|", kanPlayers), maxPlayersPerTeam);
                    return;
                }

                kanPlayers.Remove(foundPlayer);
                ayPlayers.Add(foundPlayer);
            }
        }

        SyncTeamsClientRpc(string.Join("|", ayPlayers), string.Join("|", kanPlayers), maxPlayersPerTeam);
    }

    [ClientRpc]
    private void SyncTeamsClientRpc(string ayTeamData, string kanTeamData, int teamLimit)
    {
        maxPlayersPerTeam = Mathf.Max(1, teamLimit);

        ayPlayers.Clear();
        kanPlayers.Clear();

        if (!string.IsNullOrEmpty(ayTeamData))
            ayPlayers.AddRange(ayTeamData.Split('|'));

        if (!string.IsNullOrEmpty(kanTeamData))
            kanPlayers.AddRange(kanTeamData.Split('|'));

        RefreshUI();
    }

    private void RefreshUI()
    {
        ClearContent(ayContent);
        ClearContent(kanContent);

        foreach (string player in ayPlayers)
            CreatePlayerRow(ayContent, player);

        foreach (string player in kanPlayers)
            CreatePlayerRow(kanContent, player);
    }

    private void CreatePlayerRow(Transform parent, string playerName)
    {
        if (parent == null || playerRowPrefab == null) return;

        GameObject row = Instantiate(playerRowPrefab, parent);

        TMP_Text text = row.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.text = playerName;
    }

    private void ClearContent(Transform content)
    {
        if (content == null) return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    private string GetTemporaryLocalName()
    {
        if (NetworkManager.Singleton == null)
            return "Player";

        return "Player" + NetworkManager.Singleton.LocalClientId;
    }

    public bool IsLocalPlayerAyTeam()
    {
        if (NetworkManager.Singleton == null) return false;

        string suffix = $"({NetworkManager.Singleton.LocalClientId})";

        foreach (string player in ayPlayers)
        {
            if (player.EndsWith(suffix))
                return true;
        }

        return false;
    }

    public bool IsLocalPlayerKanTeam()
    {
        if (NetworkManager.Singleton == null) return false;

        string suffix = $"({NetworkManager.Singleton.LocalClientId})";

        foreach (string player in kanPlayers)
        {
            if (player.EndsWith(suffix))
                return true;
        }

        return false;
    }
}