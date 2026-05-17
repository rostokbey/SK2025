using System;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Optional")]
    [SerializeField] private JoinCodeUI joinCodeUI;
    [SerializeField] private SceneUIController sceneUIController;

    [Header("Battle Room")]
    [SerializeField] private GameObject battleRoomPanel;
    [SerializeField] private TMP_Text hostNameText;
    [SerializeField] private TMP_Text clientNameText;
    [SerializeField] private TMP_Text hostTeamText;
    [SerializeField] private TMP_Text clientTeamText;

    [Header("Match Buttons")]
    [SerializeField] private GameObject matchButtonsPanel;

    [Header("Relay")]
    [Tooltip("Host hariç bağlanabilecek maksimum oyuncu sayısı. 1v1 için 1, 2v2 için 3, 3v3 için 5.")]
    [SerializeField] private int maxConnections = 5;

    private UnityTransport _transport;
    private static bool _servicesInitialized = false;

    private void Awake()
    {
        if (hostButton)
            hostButton.onClick.AddListener(HostGame);

        if (joinButton)
            joinButton.onClick.AddListener(JoinGame);

        if (NetworkManager.Singleton != null)
            _transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (sceneUIController == null)
            sceneUIController = FindObjectOfType<SceneUIController>(true);

        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(false);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(false);
    }

    public async void HostGame()
    {
        try
        {
            await EnsureUnityServices();

            Allocation allocation =
                await RelayService.Instance.CreateAllocationAsync(maxConnections);

            string joinCode =
                await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            RelayServerData relayServerData =
                new RelayServerData(allocation, "dtls");

            _transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();

            Debug.Log($"[Lobby] Host started. JoinCode = {joinCode}");

            joinCodeUI?.SetCode(joinCode);

            if (battleRoomPanel != null)
                battleRoomPanel.SetActive(true);

            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(true);

            if (hostNameText != null)
                hostNameText.text = GetTemporaryPlayerName();

            if (clientNameText != null)
                clientNameText.text = "Oyuncu bekleniyor...";

            if (hostTeamText != null)
                hostTeamText.text = "A Takımı";

            if (clientTeamText != null)
                clientTeamText.text = "B Takımı";
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] HostGame error: " + e);
        }
    }

    public async void JoinGame()
    {
        try
        {
            await EnsureUnityServices();

            string joinCode =
                (joinCodeInput ? joinCodeInput.text : "")
                .Trim()
                .ToUpperInvariant();

            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogWarning("[Lobby] Join code boş.");
                return;
            }

            JoinAllocation joinAllocation =
                await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData =
                new RelayServerData(joinAllocation, "dtls");

            _transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();

            Debug.Log($"[Lobby] Joined game with code: {joinCode}");

            joinCodeUI?.SetCode(joinCode);

            if (battleRoomPanel != null)
                battleRoomPanel.SetActive(true);

            // Client savaş başlatamaz, sadece host başlatır.
            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(false);

            if (hostNameText != null)
                hostNameText.text = "Host";

            if (clientNameText != null)
                clientNameText.text = GetTemporaryPlayerName();

            if (hostTeamText != null)
                hostTeamText.text = "A Takımı";

            if (clientTeamText != null)
                clientTeamText.text = "B Takımı";
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] JoinGame error: " + e);
        }
    }

    public void ClearJoinCodeUI()
    {
        joinCodeUI?.Clear();

        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(false);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(false);

        if (hostNameText != null)
            hostNameText.text = "";

        if (clientNameText != null)
            clientNameText.text = "";

        if (hostTeamText != null)
            hostTeamText.text = "";

        if (clientTeamText != null)
            clientTeamText.text = "";
    }

    private async System.Threading.Tasks.Task EnsureUnityServices()
    {
        if (_servicesInitialized)
            return;

        try
        {
            await UnityServices.InitializeAsync();

            Debug.Log("[UnityServices] Initialized");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log("[Auth] Signed in anonymously");
                Debug.Log("[Auth] PlayerID: " +
                          AuthenticationService.Instance.PlayerId);
            }

            _servicesInitialized = true;
        }
        catch (Exception e)
        {
            Debug.LogError("[UnityServices] Init/Auth error: " + e);
        }
    }

    private string GetTemporaryPlayerName()
    {
        string playerId = AuthenticationService.Instance.PlayerId;

        if (string.IsNullOrEmpty(playerId))
            return "Player000000";

        int hash = Mathf.Abs(playerId.GetHashCode());
        int number = hash % 1000000;

        return "Player" + number.ToString("000000");
    }
}