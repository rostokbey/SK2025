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
    public Button readyButton;

    private readonly List<string> ayPlayers = new();
    private readonly List<string> kanPlayers = new();
    private readonly Dictionary<ulong, bool> readyStates = new();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (switchTeamButton != null)
            switchTeamButton.onClick.AddListener(SwitchLocalPlayerTeam);

        if (readyButton != null)
            readyButton.onClick.AddListener(ToggleLocalReady);
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
            SyncTeams();
        else if (IsClient)
            SetMaxPlayersPerTeamServerRpc(maxPlayersPerTeam);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetMaxPlayersPerTeamServerRpc(int value)
    {
        maxPlayersPerTeam = Mathf.Max(1, value);
        SyncTeams();
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

        readyStates[clientId] = false;

        SyncTeams();
    }

    public void SwitchLocalPlayerTeam()
    {
        if (AreAllPlayersReady())
            return;

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
                SyncTeams();
                return;
            }

            ayPlayers.Remove(foundPlayer);
            kanPlayers.Add(foundPlayer);
            readyStates[clientId] = false;
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
                    SyncTeams();
                    return;
                }

                kanPlayers.Remove(foundPlayer);
                ayPlayers.Add(foundPlayer);
                readyStates[clientId] = false;
            }
        }

        SyncTeams();
    }

    public void ToggleLocalReady()
    {
        if (!IsClient) return;

        ToggleReadyServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        bool current = readyStates.ContainsKey(clientId) && readyStates[clientId];
        readyStates[clientId] = !current;

        SyncTeams();
    }

    public bool AreTeamsReady()
    {
        return ayPlayers.Count >= maxPlayersPerTeam &&
               kanPlayers.Count >= maxPlayersPerTeam;
    }

    public bool AreAllPlayersReady()
    {
        if (!AreTeamsReady())
            return false;

        foreach (string player in ayPlayers)
        {
            ulong id = ExtractClientId(player);
            if (!readyStates.ContainsKey(id) || !readyStates[id])
                return false;
        }

        foreach (string player in kanPlayers)
        {
            ulong id = ExtractClientId(player);
            if (!readyStates.ContainsKey(id) || !readyStates[id])
                return false;
        }

        return true;
    }

    private void SyncTeams()
    {
        string readyData = BuildReadyData();

        SyncTeamsClientRpc(
            string.Join("|", ayPlayers),
            string.Join("|", kanPlayers),
            readyData,
            maxPlayersPerTeam
        );
    }

    [ClientRpc]
    private void SyncTeamsClientRpc(string ayTeamData, string kanTeamData, string readyData, int teamLimit)
    {
        maxPlayersPerTeam = Mathf.Max(1, teamLimit);

        ayPlayers.Clear();
        kanPlayers.Clear();
        readyStates.Clear();

        if (!string.IsNullOrEmpty(ayTeamData))
            ayPlayers.AddRange(ayTeamData.Split('|'));

        if (!string.IsNullOrEmpty(kanTeamData))
            kanPlayers.AddRange(kanTeamData.Split('|'));

        ParseReadyData(readyData);

        RefreshUI();
        UpdateReadyButtonText();
        UpdateSwitchTeamButtonState();

        LobbyUIManager lobby = FindObjectOfType<LobbyUIManager>(true);
        if (lobby != null)
            lobby.RefreshStartBattleButton();
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
        {
            ulong id = ExtractClientId(playerName);
            bool isReady = readyStates.ContainsKey(id) && readyStates[id];

            bool isHostPlayer = id == 0;
            string hostIcon = isHostPlayer ? " 👑" : "";

            text.text = $"{playerName}{hostIcon} {(isReady ? "✅" : "⏳")}";
        }
    }

    private void ClearContent(Transform content)
    {
        if (content == null) return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    private string BuildReadyData()
    {
        List<string> parts = new();

        foreach (var pair in readyStates)
            parts.Add($"{pair.Key}:{(pair.Value ? 1 : 0)}");

        return string.Join("|", parts);
    }

    private void ParseReadyData(string readyData)
    {
        if (string.IsNullOrEmpty(readyData))
            return;

        string[] parts = readyData.Split('|');

        foreach (string part in parts)
        {
            string[] split = part.Split(':');

            if (split.Length != 2)
                continue;

            if (ulong.TryParse(split[0], out ulong clientId))
            {
                readyStates[clientId] = split[1] == "1";
            }
        }
    }

    private ulong ExtractClientId(string playerName)
    {
        int start = playerName.LastIndexOf('(');
        int end = playerName.LastIndexOf(')');

        if (start < 0 || end < 0 || end <= start)
            return 999999;

        string idText = playerName.Substring(start + 1, end - start - 1);

        if (ulong.TryParse(idText, out ulong id))
            return id;

        return 999999;
    }

    private void UpdateReadyButtonText()
    {
        if (readyButton == null || NetworkManager.Singleton == null)
            return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        bool isReady = readyStates.ContainsKey(localId) && readyStates[localId];

        TMP_Text text = readyButton.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.text = isReady ? "HAZIR ✅" : "HAZIR OL";
    }

    private void UpdateSwitchTeamButtonState()
    {
        if (switchTeamButton == null)
            return;

        bool allReady = AreAllPlayersReady();

        switchTeamButton.interactable = !allReady;
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