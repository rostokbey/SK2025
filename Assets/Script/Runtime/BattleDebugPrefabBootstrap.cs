using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gerçek 3D karakter modeli bulunmadığında Resources/Battle/DebugBattleCharacter
/// prefabını yükler ve Netcode'a kaydeder. Prefab Editor scripti tarafından üretilir.
/// </summary>
public static class BattleDebugPrefabBootstrap
{
    private const string ResourcePath = "Battle/DebugBattleCharacter";
    private static GameObject cachedPrefab;
    private static NetworkManager registeredManager;

    public static GameObject GetPrefab()
    {
        if (cachedPrefab == null)
            cachedPrefab = Resources.Load<GameObject>(ResourcePath);
        return cachedPrefab;
    }

    public static void EnsureRegistered()
    {
        NetworkManager manager = NetworkManager.Singleton;
        GameObject prefab = GetPrefab();

        if (manager == null || prefab == null || registeredManager == manager)
            return;

        if (manager.IsListening)
        {
            // ForceSamePrefabs açıkken ağ başladıktan sonra liste değiştirilemez.
            // Editor üreticisi prefabı NetworkPrefabsList'e önceden ekler.
            registeredManager = manager;
            return;
        }

        try
        {
            manager.AddNetworkPrefab(prefab);
            registeredManager = manager;
            Debug.Log("[DEBUG PREFAB] Geçici savaş karakteri Netcode'a kaydedildi.");
        }
        catch (Exception exception)
        {
            // Prefab Inspector üzerinden önceden kayıtlıysa AddNetworkPrefab yinelenebilir.
            // Bu durumda prefab yine kullanılabilir; gerçek hatayı logda görünür bırakıyoruz.
            registeredManager = manager;
            Debug.LogWarning("[DEBUG PREFAB] Kayıt bilgisi: " + exception.Message);
        }
    }
}
