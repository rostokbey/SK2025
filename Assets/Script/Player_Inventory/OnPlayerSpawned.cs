using Unity.Netcode;
using UnityEngine;

public class OnPlayerSpawned : MonoBehaviour
{
    public GameObject enemyCardPrefab;

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += (clientId) =>
        {
            if (!NetworkManager.Singleton.IsHost)
            {
                GameObject enemy = Instantiate(enemyCardPrefab, new Vector3(3, 0, 0), Quaternion.identity);
                enemy.GetComponent<NetworkObject>().Spawn();
            }
        };
    }
}
