using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Kayıt dosyasını saran zarf: ham veri (JSON string olarak) + bütünlük imzası.
/// "data" bilerek string olarak saklanır (iç içe JSON), böylece imzayı
/// hesaplarken serileştirme farklarından (alan sırası vb.) etkilenmeyiz -
/// imzaladığımız bayt dizisiyle doğruladığımız bayt dizisi birebir aynı olur.
/// </summary>
[Serializable]
internal class SignedEnvelope
{
    public int v = 1;
    public string data;
    public string sig;
}

public static class SaveSystem
{
    // --- Envanter kaydı ---
    private const string InventoryFilePrefix = "inv_";
    private const string InventoryPurpose = "inventory";
    private static string InventoryPathFor(string profileId) =>
        Path.Combine(Application.persistentDataPath, $"{InventoryFilePrefix}{profileId}.json");

    // --- Deste kaydı ---
    private const string DeckFilePrefix = "decks_";
    private const string DeckPurpose = "decks";
    private static string DeckPathFor(string profileId) =>
        Path.Combine(Application.persistentDataPath, $"{DeckFilePrefix}{profileId}.json");

    // --- Envanter Fonksiyonları ---
    public static void SaveInventory(string profileId, InventorySaveData data)
    {
        WriteSigned(InventoryPathFor(profileId), data, profileId, InventoryPurpose);
        Debug.Log($"[SaveSystem] Envanter kaydedildi (imzalı): {InventoryPathFor(profileId)}");
    }

    public static InventorySaveData LoadInventory(string profileId)
    {
        var path = InventoryPathFor(profileId);
        if (!File.Exists(path))
        {
            Debug.Log($"[SaveSystem] Envanter kaydı yok, yeni oluşturuluyor: {path}");
            return new InventorySaveData();
        }

        if (TryReadSigned<InventorySaveData>(path, profileId, InventoryPurpose, out var data, out var reason))
            return data ?? new InventorySaveData();

        Debug.LogWarning($"[SaveSystem] Envanter kaydı reddedildi ({reason}). Boş envanterle devam ediliyor: {path}");
        QuarantineTamperedFile(path);
        return new InventorySaveData();
    }

    // --- Deste Fonksiyonları ---
    public static void SaveDecks(string profileId, DeckSaveData data)
    {
        WriteSigned(DeckPathFor(profileId), data, profileId, DeckPurpose);
        Debug.Log($"[SaveSystem] Desteler kaydedildi (imzalı): {DeckPathFor(profileId)}");
    }

    public static DeckSaveData LoadDecks(string profileId)
    {
        var path = DeckPathFor(profileId);
        if (!File.Exists(path))
        {
            Debug.Log($"[SaveSystem] Deste kaydı yok, yeni oluşturuluyor: {path}");
            return new DeckSaveData();
        }

        if (TryReadSigned<DeckSaveData>(path, profileId, DeckPurpose, out var data, out var reason))
            return data ?? new DeckSaveData();

        Debug.LogWarning($"[SaveSystem] Deste kaydı reddedildi ({reason}). Boş destelerle devam ediliyor: {path}");
        QuarantineTamperedFile(path);
        return new DeckSaveData();
    }

    // --- Ortak imzalı yazma/okuma ---

    private static void WriteSigned<T>(string path, T data, string profileId, string purpose)
    {
        string innerJson = JsonUtility.ToJson(data, false);
        string signature = SaveIntegrity.Sign(innerJson, profileId, purpose);

        var envelope = new SignedEnvelope { data = innerJson, sig = signature };
        string envelopeJson = JsonUtility.ToJson(envelope, true);

        File.WriteAllText(path, envelopeJson, Encoding.UTF8);
    }

    /// <summary>
    /// Eski (imzasız, düz JSON) kayıt dosyalarını da okuyabilmek için basit bir
    /// geriye dönük uyumluluk: dosya SignedEnvelope formatında değilse, eski
    /// formatta doğrudan T olarak parse etmeyi dener. Bu sadece var olan
    /// oyuncuların kayıtlarını kaybetmemek içindir; bir sonraki kaydetmede
    /// dosya otomatik olarak imzalı formata yükseltilir.
    /// </summary>
    private static bool TryReadSigned<T>(string path, string profileId, string purpose, out T data, out string reason)
    {
        data = default;
        reason = null;

        string raw;
        try
        {
            raw = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            reason = "dosya okunamadı: " + ex.Message;
            return false;
        }

        SignedEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<SignedEnvelope>(raw);
        }
        catch
        {
            envelope = null;
        }

        if (envelope != null && !string.IsNullOrEmpty(envelope.data) && !string.IsNullOrEmpty(envelope.sig))
        {
            if (!SaveIntegrity.Verify(envelope.data, envelope.sig, profileId, purpose))
            {
                reason = "imza doğrulanamadı (dosya kurcalanmış olabilir)";
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<T>(envelope.data);
                return true;
            }
            catch (Exception ex)
            {
                reason = "imzalı içerik ayrıştırılamadı: " + ex.Message;
                return false;
            }
        }

        // Geriye dönük uyumluluk: eski imzasız formatta olabilir.
        try
        {
            data = JsonUtility.FromJson<T>(raw);
            Debug.LogWarning("[SaveSystem] Eski (imzasız) kayıt formatı tespit edildi, bir sonraki kaydetmede imzalı formata yükseltilecek.");
            return data != null;
        }
        catch (Exception ex)
        {
            reason = "bilinmeyen dosya formatı: " + ex.Message;
            return false;
        }
    }

    private static void QuarantineTamperedFile(string path)
    {
        try
        {
            string quarantinePath = path + ".tampered_" + DateTime.UtcNow.Ticks;
            File.Move(path, quarantinePath);
            Debug.LogWarning($"[SaveSystem] Şüpheli dosya karantinaya alındı: {quarantinePath}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SaveSystem] Şüpheli dosya karantinaya alınamadı: " + ex.Message);
        }
    }
}
