using System.IO;
using System.Text;
using UnityEngine;

public static class SaveSystem
{
    // Mevcut envanter kaydý için
    private const string InventoryFilePrefix = "inv_";
    private static string InventoryPathFor(string profileId)
    {
        return Path.Combine(Application.persistentDataPath, $"{InventoryFilePrefix}{profileId}.json");
    }

    // YENÝ EKLENDÝ: Deste kaydý için
    private const string DeckFilePrefix = "decks_";
    private static string DeckPathFor(string profileId)
    {
        return Path.Combine(Application.persistentDataPath, $"{DeckFilePrefix}{profileId}.json");
    }

    // --- Envanter Fonksiyonlarý (Mevcut) ---
    public static void SaveInventory(string profileId, InventorySaveData data)
    {
        var json = JsonUtility.ToJson(data, true);
        File.WriteAllText(InventoryPathFor(profileId), json, Encoding.UTF8);
        Debug.Log($"[SaveSystem] Envanter kaydedildi: {InventoryPathFor(profileId)}");
    }

    public static InventorySaveData LoadInventory(string profileId)
    {
        var path = InventoryPathFor(profileId);
        if (!File.Exists(path))
        {
            Debug.Log($"[SaveSystem] Envanter kaydý yok, yeni oluþturuluyor: {path}");
            return new InventorySaveData();
        }
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonUtility.FromJson<InventorySaveData>(json) ?? new InventorySaveData();
    }

    // --- YENÝ DESTE FONKSÝYONLARI ---
    public static void SaveDecks(string profileId, DeckSaveData data)
    {
        var json = JsonUtility.ToJson(data, true);
        File.WriteAllText(DeckPathFor(profileId), json, Encoding.UTF8);
        Debug.Log($"[SaveSystem] Desteler kaydedildi: {DeckPathFor(profileId)}");
    }

    public static DeckSaveData LoadDecks(string profileId)
    {
        var path = DeckPathFor(profileId);
        if (!File.Exists(path))
        {
            Debug.Log($"[SaveSystem] Deste kaydý yok, yeni oluþturuluyor: {path}");
            return new DeckSaveData();
        }
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonUtility.FromJson<DeckSaveData>(json) ?? new DeckSaveData();
    }
}