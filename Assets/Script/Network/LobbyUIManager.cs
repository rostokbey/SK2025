using System;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;           // UnityTransport + RelayServerData
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;                   // UnityServices.InitializeAsync
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
    [SerializeField] private JoinCodeUI joinCodeUI;                // Join code göster/kopyala paneli
    [SerializeField] private SceneUIController sceneUIController;  // Panel geçişleri için (Inspector’dan ver)

    [Header("Relay")]
    [Tooltip("Oda başına maksimum bağlanacak oyuncu sayısı (host hariç).")]
    [SerializeField] private int maxConnections = 3;               // 1v1 için 1; 2v2 için 3; 3v3 için 5 vb.

    private UnityTransport _transport;

    void Awake()
    {
        if (hostButton) hostButton.onClick.AddListener(HostGame);
        if (joinButton) joinButton.onClick.AddListener(JoinGame);

        _transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        // Sahne referansı verilmemişse bulmaya çalış
        if (sceneUIController == null)
            sceneUIController = FindObjectOfType<SceneUIController>(true);
    }

    // -------------------------
    // HOST: Oda/Lobi oluşturur
    // -------------------------
    public async void HostGame()
    {
        try
        {
            await EnsureUnityServices();

            // 1) Relay allocation & join code
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // 2) UnityTransport'u Relay'e bağla
            var relayServerData = new RelayServerData(allocation, "dtls");
            _transport.SetRelayServerData(relayServerData);

            // 3) Netcode başlat (host)
            NetworkManager.Singleton.StartHost();
            Debug.Log($"[Lobby] Host started. JoinCode = {joinCode}");

            // 4) Join code'u UI'de göster
            joinCodeUI?.SetCode(joinCode);

            // 5) İstediğin panele yönlendir (ör. DeckBuilder)
            sceneUIController?.ShowOnly(SceneUIController.TargetPanel.DeckBuilder);
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] HostGame error: " + e);
        }
    }

    // -----------------------------------
    // JOIN: Mevcut lobiye katılım yapar
    // -----------------------------------
    public async void JoinGame()
    {
        try
        {
            await EnsureUnityServices();

            string joinCode = (joinCodeInput ? joinCodeInput.text : "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogWarning("[Lobby] JoinGame: join code boş.");
                return;
            }

            // 1) Relay join allocation
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // 2) UnityTransport'u Relay'e bağla
            var relayServerData = new RelayServerData(joinAllocation, "dtls");
            _transport.SetRelayServerData(relayServerData);

            // 3) Netcode başlat (client)
            NetworkManager.Singleton.StartClient();
            Debug.Log($"[Lobby] Joined game with code: {joinCode}");

            // 4) (İsteğe bağlı) Oyuncunun girdiği kodu göster
            joinCodeUI?.SetCode(joinCode);

            // 5) İstediğin panele yönlendir (ör. DeckBuilder)
            sceneUIController?.ShowOnly(SceneUIController.TargetPanel.DeckBuilder);
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] JoinGame error: " + e);
        }
    }

    // Çıkış/Disconnect anında çağır—Join Code panelini gizler
    public void ClearJoinCodeUI()
    {
        joinCodeUI?.Clear();
    }

    // Unity Services init guard
    private static bool _servicesInitialized = false;
    private async System.Threading.Tasks.Task EnsureUnityServices()
    {
        if (_servicesInitialized) return;
        try
        {
            await UnityServices.InitializeAsync();
        }
        catch (Exception)
        {
            // zaten init edilmiş olabilir; sorun değil
        }
        _servicesInitialized = true;
    }
}
