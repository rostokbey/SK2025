using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Threading.Tasks;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance;

    public string joinCode;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void Start()
    {
        // Unity Services başlatma ve oturum açma artık AccountService'te (tek yer).
        // Hata olursa AccountService.LastError'a yazılır; CreateRelay/JoinRelay yeniden dener.
        await AccountService.EnsureReadyAsync();
    }

    public async Task<string> CreateRelay()
    {
        // Oturum hazır olmadan Relay çağrısı yapılırsa "oturum yok" hatası alınır.
        if (!await AccountService.EnsureReadyAsync())
        {
            Debug.LogError($"CreateRelay: hesap servisi hazır değil. {AccountService.Instance?.LastError}");
            return null;
        }

        try
        {
            // Host hariç bağlanabilecek oyuncu sayısı. Eskiden sabit 2'ydi: 2v2 (3 client) ve
            // 3v3 (5 client) maçlarında son oyuncular bağlanamazdı. En az 2 (eski davranış).
            int maxConnections = Mathf.Clamp(LobbyTeamManager.SelectedPlayersPerTeam * 2 - 1, 2, 5);
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            utp.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                false
            );

            NetworkManager.Singleton.StartHost();
            return joinCode;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"CreateRelay Error: {e}");
            return null;
        }
    }

    public async Task<bool> JoinRelay(string joinCode)
    {
        if (!await AccountService.EnsureReadyAsync())
        {
            Debug.LogError($"JoinRelay: hesap servisi hazır değil. {AccountService.Instance?.LastError}");
            return false;
        }

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            utp.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                false
            );

            NetworkManager.Singleton.StartClient();
            return true;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"JoinRelay Error: {e}");
            return false;
        }
    }
}
