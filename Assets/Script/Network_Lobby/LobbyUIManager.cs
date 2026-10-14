using System;
using System.Collections;
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

    [Header("Join Code Panel")]
    [SerializeField] private Button joinCodeCloseButton;

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
    private bool isConnecting = false;

    private Coroutine warningClearRoutine;
    private const float WarningDisplaySeconds = 3f;

    private void Awake()
    {
        if (hostButton != null)
            hostButton.onClick.AddListener(OpenMatchSizeSelection);

        if (joinButton != null)
            joinButton.onClick.AddListener(JoinGame);

        if (startBattleButton != null)
            startBattleButton.onClick.AddListener(StartBattleFromLobby);

        if (NetworkManager.Singleton != null)
        {
            transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        SetLobbyStartState();
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        Debug.Log(
            $"[Lobby] Client bağlandı. ClientId={clientId}, " +
            $"LocalClientId={NetworkManager.Singleton.LocalClientId}, " +
            $"IsHost={NetworkManager.Singleton.IsHost}");

        if (clientId == NetworkManager.Singleton.LocalClientId)
            ShowWarning("Sunucuya bağlantı tamamlandı.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        string reason = NetworkManager.Singleton.DisconnectReason;
        string message = string.IsNullOrEmpty(reason) ? "Sebep belirtilmedi" : reason;

        Debug.LogError(
            $"[Lobby] Client bağlantısı kesildi. ClientId={clientId}, Sebep={message}");

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            LobbyTeamManager.ResetMatchSnapshot();
            SetLobbyStartState();
            ShowWarning("Sunucu bağlantısı kesildi: " + message);
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void SetLobbyStartState()
    {
        isStartingBattle = false;
        isConnecting = false;

        if (hostButton != null)
        {
            hostButton.gameObject.SetActive(true);
            hostButton.interactable = true;
        }

        if (joinButton != null)
        {
            joinButton.gameObject.SetActive(true);
            joinButton.interactable = true;
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.text = "";
            joinCodeInput.gameObject.SetActive(true);
        }

        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(false);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(false);

        if (teamPanel != null)
            teamPanel.SetActive(false);

        if (startBattleButton != null)
        {
            startBattleButton.gameObject.SetActive(false);
            startBattleButton.interactable = false;
        }

        if (joinCodeCloseButton != null)
            joinCodeCloseButton.gameObject.SetActive(false);

        if (countdownText != null)
        {
            countdownText.text = "";
            countdownText.gameObject.SetActive(false);
        }

        ShowWarning("");
    }

    public void OnLobbyPanelOpened()
    {
        // NetworkManager hâlâ dinlemedeyse kapat (eski bağlantı kalıntısı)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[Lobby] Eski bağlantı bulundu, kapatılıyor.");
            NetworkManager.Singleton.Shutdown();
        }

        // Butonları ve panelleri başlangıç durumuna getir
        SetLobbyStartState();

        Debug.Log("[Lobby] Lobi paneli açıldı, butonlar sıfırlandı.");
    }

    public void OpenMatchSizeSelection()
    {
        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(true);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(true);

        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

        if (teamPanel != null)
            teamPanel.SetActive(false);

        if (hostButton != null)
            hostButton.gameObject.SetActive(false);

        if (joinButton != null)
            joinButton.gameObject.SetActive(false);

        if (joinCodeInput != null)
        {
            joinCodeInput.text = "";
            joinCodeInput.gameObject.SetActive(false);
        }

        if (startBattleButton != null)
            startBattleButton.gameObject.SetActive(false);

        ShowWarning("");
    }

    public async void HostGame()
    {
        if (isConnecting)
        {
            Debug.LogWarning("[Lobby] Host bağlantısı zaten başlatılıyor.");
            return;
        }

        if (!CanStartNetwork("Host"))
            return;

        if (!HasEnoughCardsForBattle())
            return;

        isConnecting = true;

        try
        {
            await EnsureUnityServices();

            // Relay değeri host hariç katılımcı sayısıdır:
            // 1v1 => 1, 2v2 => 3, 3v3 => 5 bağlantı.
            int requiredConnections = Mathf.Clamp(
                LobbyTeamManager.SelectedPlayersPerTeam * 2 - 1,
                1,
                maxConnections);
            Allocation allocation =
                await RelayService.Instance.CreateAllocationAsync(requiredConnections);

            string joinCode =
                await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                false
            );

            if (!NetworkManager.Singleton.StartHost())
                throw new Exception("NetworkManager host olarak başlatılamadı.");

            joinCodeUI?.SetCode(joinCode);

            // Host, kendi oluşturduğu kodu girmez; sadece kodu görür/kopyalar.
            if (joinCodeInput != null)
            {
                joinCodeInput.text = "";
                joinCodeInput.gameObject.SetActive(false);
            }

            if (hostButton != null)
                hostButton.gameObject.SetActive(false);

            if (joinButton != null)
                joinButton.gameObject.SetActive(false);

            if (joinCodePanel != null)
                joinCodePanel.SetActive(true);

            if (joinCodeCloseButton != null)
                joinCodeCloseButton.gameObject.SetActive(false);

            if (battleRoomPanel != null)
                battleRoomPanel.SetActive(true);

            if (teamPanel != null)
                teamPanel.SetActive(true);

            if (matchButtonsPanel != null)
                matchButtonsPanel.SetActive(false);

            RefreshStartBattleButton();

            Debug.Log(
                $"[Lobby] {LobbyTeamManager.SelectedPlayersPerTeam}v" +
                $"{LobbyTeamManager.SelectedPlayersPerTeam} host başladı. " +
                $"Relay katılımcı kapasitesi={requiredConnections}, Join Code={joinCode}");
        }
        catch (Exception e)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            SetLobbyStartState();
            ShowWarning("Host oluşturulamadı.");
            Debug.LogError("[Lobby] HostGame hata: " + e);
        }
        finally
        {
            isConnecting = false;
        }
    }

    public async void JoinGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            ShowWarning("Siz zaten odasınız. Başka oyuncular bu kodu kullanarak katılabilir.");
            return;
        }

        if (isConnecting)
        {
            Debug.LogWarning("[Lobby] Bağlantı işlemi zaten devam ediyor.");
            return;
        }

        if (!CanStartNetwork("Client"))
            return;

        if (!HasEnoughCardsForBattle())
            return;

        isConnecting = true;

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

            transport.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                false
            );

            if (!NetworkManager.Singleton.StartClient())
                throw new Exception("NetworkManager client olarak başlatılamadı.");

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

            ShowWarning("");

            Debug.Log(
                "[Lobby] Client bağlantısı başlatıldı. " +
                "Gerçek bağlantı callback ile doğrulanacak. Kod: " + joinCode);
        }
        catch (Exception e)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            SetLobbyStartState();
            ShowWarning("Oyuna katılım başarısız.");
            Debug.LogError("[Lobby] JoinGame hata: " + e);
        }
        finally
        {
            isConnecting = false;
        }
    }

    private bool CanStartNetwork(string operation)
    {
        if (NetworkManager.Singleton == null)
        {
            ShowWarning("NetworkManager bulunamadı.");
            return false;
        }

        if (transport == null)
        {
            ShowWarning("UnityTransport bulunamadı.");
            return false;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            ShowWarning("Bu oyun örneğinde ağ zaten çalışıyor.");
            Debug.LogWarning(
                $"[Lobby] {operation} başlatılamadı: " +
                $"IsHost={NetworkManager.Singleton.IsHost}, " +
                $"IsClient={NetworkManager.Singleton.IsClient}, " +
                $"IsListening={NetworkManager.Singleton.IsListening}");
            return false;
        }

        return true;
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

    private IEnumerator StartBattleCountdown()
    {
        BroadcastBattleCountdown(3);

        yield return new WaitForSecondsRealtime(1f);

        BroadcastBattleCountdown(2);

        yield return new WaitForSecondsRealtime(1f);

        BroadcastBattleCountdown(1);

        yield return new WaitForSecondsRealtime(1f);

        BroadcastBattleCountdown(0);

        yield return new WaitForSecondsRealtime(0.5f);

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SceneManager != null)
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

    private void BroadcastBattleCountdown(int value)
    {
        if (LobbyTeamManager.Instance != null &&
            LobbyTeamManager.Instance.IsSpawned &&
            LobbyTeamManager.Instance.IsServer)
        {
            LobbyTeamManager.Instance.BroadcastBattleCountdown(value);
            return;
        }

        ShowBattleCountdown(value);
    }

    public void ShowBattleCountdown(int value)
    {
        if (countdownText == null)
            return;

        countdownText.gameObject.SetActive(true);
        countdownText.text = value > 0 ? value.ToString() : "SAVAŞ!";
    }

    public void RefreshStartBattleButton()
    {
        if (startBattleButton == null)
            return;

        bool isHost =
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsHost;

        bool canStart =
            !isStartingBattle &&
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

        if (count < BattleRules.MinimumDeckSize)
        {
            ShowWarning(
                $"Maça girmek için en az {BattleRules.MinimumDeckSize} kart gerekli. " +
                $"Şu an: {count}/{BattleRules.MinimumDeckSize}"
            );
            return false;
        }

        ShowWarning("");
        return true;
    }

    public void LeaveRoom()
    {
        Debug.LogError(
            "[Lobby] LeaveRoom çağrıldı!\n" +
            Environment.StackTrace);

        isStartingBattle = false;
        isConnecting = false;

        if (countdownText != null)
        {
            countdownText.text = "";
            countdownText.gameObject.SetActive(false);
        }

        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsHost ||
                NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        // Ağ kapanması için bekle
        StartCoroutine(ShutdownWaitThenReset());

        if (joinCodeUI != null)
            joinCodeUI.Clear();

        LobbyTeamManager.ResetMatchSnapshot();

        if (joinCodeInput != null)
        {
            joinCodeInput.text = "";
            joinCodeInput.gameObject.SetActive(true);
        }

        if (joinCodePanel != null)
            joinCodePanel.SetActive(false);

        if (battleRoomPanel != null)
            battleRoomPanel.SetActive(false);

        if (teamPanel != null)
            teamPanel.SetActive(false);

        if (matchButtonsPanel != null)
            matchButtonsPanel.SetActive(false);

        if (startBattleButton != null)
        {
            startBattleButton.gameObject.SetActive(false);
            startBattleButton.interactable = false;
        }

        if (hostButton != null)
            hostButton.gameObject.SetActive(true);

        if (joinButton != null)
            joinButton.gameObject.SetActive(true);

        ShowWarning("");
    }

    private IEnumerator ShutdownWaitThenReset()
    {
        // Shutdown anında bitmiyor, birkaç frame bekleyip tekrar dene
        float timeout = 3f;
        float elapsed = 0f;

        while (NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsListening &&
               elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Hâlâ dinlemedeyse zorla kapat
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[Lobby] Shutdown tamamlanmadı, zorla kapatılıyor.");
            NetworkManager.Singleton.Shutdown();
        }
    }

    public void ClearJoinCodeUI()
    {
        // Bu buton kullanıcıyı oda görünümünden ana menüye döndürüyor.
        // Sadece panelleri kapatmak NetworkManager'ı dinlemede bırakıyordu;
        // sonraki Oda Kur/Katıl denemesi "ağ zaten çalışıyor" hatası veriyordu.
        LeaveRoom();
    }

    private void ShowWarning(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Debug.LogWarning(message);

        if (warningText == null)
            return;

        if (warningClearRoutine != null)
        {
            StopCoroutine(warningClearRoutine);
            warningClearRoutine = null;
        }

        warningText.text = message;

        if (!string.IsNullOrEmpty(message))
            warningClearRoutine = StartCoroutine(ClearWarningAfterDelay());
    }

    private IEnumerator ClearWarningAfterDelay()
    {
        yield return new WaitForSecondsRealtime(WarningDisplaySeconds);

        if (warningText != null)
            warningText.text = "";

        warningClearRoutine = null;
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