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
using UnityEngine.SceneManagement;

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
    [SerializeField] private GameObject teamPanel;

    [Header("Battle Start")]
    [SerializeField] private Button startBattleButton;
    [SerializeField] private string battleSceneName = "BattleScene";

    [Header("Kod UI")]
    [SerializeField] private JoinCodeUI joinCodeUI;

    [Header("Warning UI")]
    [SerializeField] private TMP_Text warningText;

    [Header("Relay")]
    [SerializeField] private int maxConnections = 5;

    [Header("Countdown UI")]
    [SerializeField] private TMP_Text countdownText;

    private UnityTransport transport;
    private static bool servicesInitialized = false;

    private bool isStartingBattle = false;

    private void Awake()
    {
        if (hostButton != null)
            hostButton.onClick.AddListener(OpenMatchSizeSelection);

        if (joinButton != null)
            joinButton.onClick.AddListener(JoinGame);

        if (startBattleButton != null)
            startBattleButton.onClick.AddListener(StartBattleFromLobby);

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

        if (startBattleButton != null)
            startBattleButton.gameObject.SetActive(false);

        ShowWarning("");
    }

    public void OpenMatchSizeSelection()
    {
        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(true);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(true);

        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

        if (hostButton != null)
            hostButton.gameObject.SetActive(false);

        if (joinButton != null)
            joinButton.gameObject.SetActive(false);

        if (startBattleButton != null)
            startBattleButton.gameObject.SetActive(false);

        ShowWarning("");
    }

    public async void HostGame()
    {
        if (!HasEnoughCardsForBattle())
            return;

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

            if (teamPanel != null)
                teamPanel.SetActive(true);

            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(true);

            RefreshStartBattleButton();


            Debug.Log("[Lobby] Host başladı. Join Code: " + joinCode);
        }
        catch (Exception e)
        {
            ShowWarning("Host oluşturulamadı.");
            Debug.LogError("[Lobby] HostGame hata: " + e);
        }
    }

    public async void JoinGame()
    {
        if (!HasEnoughCardsForBattle())
            return;

        try
        {
            await EnsureUnityServices();

            string joinCode = joinCodeInput != null
                ? joinCodeInput.text.Trim().ToUpperInvariant()
                : "";

            if (string.IsNullOrEmpty(joinCode))
            {
                ShowWarning("Join code boş.");
                return;
            }

            JoinAllocation joinAllocation =
                await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData =
                new RelayServerData(joinAllocation, "dtls");

            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();

            joinCodeUI?.SetCode(joinCode);

            if (hostButton != null)
                hostButton.gameObject.SetActive(false);

            if (joinButton != null)
                joinButton.gameObject.SetActive(false);

            if (joinCodePanel != null)
                joinCodePanel.SetActive(false);

            if (battleRoomPanel != null)
                battleRoomPanel.SetActive(true);

            if (teamPanel != null)
                teamPanel.SetActive(true);

            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(false);

            if (startBattleButton != null)
                startBattleButton.gameObject.SetActive(false);

            Debug.Log("[Lobby] Join başarılı. Kod: " + joinCode);
        }
        catch (Exception e)
        {
            ShowWarning("Oyuna katılım başarısız.");
            Debug.LogError("[Lobby] JoinGame hata: " + e);
        }
    }

    private void StartBattleFromLobby()
    {
        if (isStartingBattle)
            return;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
        {
            ShowWarning("Savaşı sadece host başlatabilir.");
            return;
        }

        if (LobbyTeamManager.Instance == null || !LobbyTeamManager.Instance.AreTeamsReady())
        {
            ShowWarning("Savaş başlatılamaz. İki takım da dolu olmalı.");
            return;
        }

        if (!LobbyTeamManager.Instance.AreAllPlayersReady())
        {
            ShowWarning("Savaş başlatılamaz. Tüm oyuncular hazır olmalı.");
            return;
        }

        isStartingBattle = true;

        if (startBattleButton != null)
            startBattleButton.interactable = false;

        StartCoroutine(StartBattleCountdown());
    }

    private System.Collections.IEnumerator StartBattleCountdown()
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "3";
        }

        yield return new WaitForSeconds(1f);

        if (countdownText != null)
            countdownText.text = "2";

        yield return new WaitForSeconds(1f);

        if (countdownText != null)
            countdownText.text = "1";

        yield return new WaitForSeconds(1f);

        if (countdownText != null)
            countdownText.text = "SAVAŞ!";

        yield return new WaitForSeconds(0.5f);

        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                battleSceneName,
                LoadSceneMode.Single
            );
        }
        else
        {
            SceneManager.LoadScene(battleSceneName);
        }
    }

    public void RefreshStartBattleButton()
    {
        if (startBattleButton == null)
            return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        bool canStart =
            isHost &&
            LobbyTeamManager.Instance != null &&
            LobbyTeamManager.Instance.AreTeamsReady() &&
            LobbyTeamManager.Instance.AreAllPlayersReady();

        startBattleButton.gameObject.SetActive(isHost);
        startBattleButton.interactable = canStart;
    }

    private bool HasEnoughCardsForBattle()
    {
        var deckManager = DeckManagerObject.Instance
            ?? FindObjectOfType<DeckManagerObject>(true);

        if (deckManager == null)
        {
            ShowWarning("DeckManagerObject bulunamadı.");
            return false;
        }

        var selectedDeck = deckManager.GetSelectedCards();
        int count = selectedDeck != null ? selectedDeck.Count : 0;

        if (count < 5)
        {
            ShowWarning($"Maça girmek için en az 5 kart gerekli. Şu an: {count}/5");
            return false;
        }

        ShowWarning("");
        return true;
    }

    public void LeaveRoom()
    {
        isStartingBattle = false;

        if (countdownText != null)
        {
            countdownText.text = "";
            countdownText.gameObject.SetActive(false);
        }

        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
                NetworkManager.Singleton.Shutdown();
        }

        if (joinCodeUI != null)
            joinCodeUI.Clear();

        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(false);

        if (teamPanel != null)
            teamPanel.SetActive(false);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(false);

        if (startBattleButton != null)
            startBattleButton.gameObject.SetActive(false);

        if (hostButton != null)
            hostButton.gameObject.SetActive(true);

        if (joinButton != null)
            joinButton.gameObject.SetActive(true);



        ShowWarning("");
    }

    private void ShowWarning(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Debug.LogWarning(message);

        if (warningText != null)
            warningText.text = message;
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

        if (startBattleButton != null)
            startBattleButton.gameObject.SetActive(false);

        ShowWarning("");
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
}