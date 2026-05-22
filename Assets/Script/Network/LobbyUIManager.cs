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
    [Header("Ana Butonlar")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;

    [Header("Join Alanı")]
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Panel Alanları")]
    [SerializeField] private GameObject joinCodePanel;
    [SerializeField] private GameObject battleRoomPanel;
    [SerializeField] private GameObject matchButtonsPanel;

    [Header("Kod UI")]
    [SerializeField] private JoinCodeUI joinCodeUI;

    [Header("Battle Room Yazıları")]
    [SerializeField] private TMP_Text hostNameText;
    [SerializeField] private TMP_Text clientNameText;
    [SerializeField] private TMP_Text hostTeamText;
    [SerializeField] private TMP_Text clientTeamText;

    [Header("Relay")]
    [SerializeField] private int maxConnections = 5;

    private UnityTransport transport;
    private static bool servicesInitialized = false;

    private void Awake()
    {
        if (hostButton != null)
            hostButton.onClick.AddListener(HostGame);

        if (joinButton != null)
            joinButton.onClick.AddListener(JoinGame);

        if (NetworkManager.Singleton != null)
            transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        SetLobbyStartState();
    }

    private void SetLobbyStartState()
    {
        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

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

            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();

            joinCodeUI?.SetCode(joinCode);

            if (joinCodePanel != null)
                joinCodePanel.SetActive(true);

            if (battleRoomPanel != null)
                battleRoomPanel.SetActive(true);

            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(true);

            SetRoomTexts(true);

            Debug.Log("[Lobby] Host başladı. Join Code: " + joinCode);
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] HostGame hata: " + e);
        }
    }

    public async void JoinGame()
    {
        try
        {
            await EnsureUnityServices();

            string joinCode = joinCodeInput != null
                ? joinCodeInput.text.Trim().ToUpperInvariant()
                : "";

            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogWarning("[Lobby] Join code boş.");
                return;
            }

            JoinAllocation joinAllocation =
                await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData =
                new RelayServerData(joinAllocation, "dtls");

            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();

            joinCodeUI?.SetCode(joinCode);

            if (joinCodePanel != null)
                joinCodePanel.SetActive(false);

            if (battleRoomPanel != null)
                battleRoomPanel.SetActive(true);

            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(false);

            SetRoomTexts(false);

            Debug.Log("[Lobby] Join başarılı. Kod: " + joinCode);
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] JoinGame hata: " + e);
        }
    }

    private void SetRoomTexts(bool isHost)
    {
        if (hostNameText != null)
            hostNameText.text = isHost ? GetTemporaryPlayerName() : "Host";

        if (clientNameText != null)
            clientNameText.text = isHost ? "Oyuncu bekleniyor..." : GetTemporaryPlayerName();

        if (hostTeamText != null)
            hostTeamText.text = "A Takımı";

        if (clientTeamText != null)
            clientTeamText.text = "B Takımı";
    }

    public void ClearJoinCodeUI()
    {
        joinCodeUI?.Clear();

        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(false);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(false);
    }

    private async System.Threading.Tasks.Task EnsureUnityServices()
    {
        if (servicesInitialized)
            return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        servicesInitialized = true;

        Debug.Log("[UnityServices] Hazır.");
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