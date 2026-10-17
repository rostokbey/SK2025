using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;

/// <summary>
/// Envanter (karakterler) ve desteleri Unity Cloud Save ile hesaba (PlayerId) bağlar.
///
/// Nasıl çalışır:
///  - Yerel dosyalar (SaveSystem) olduğu gibi kalır; hızlı açılış ve çevrimdışı kullanım için önbellek görevi görür.
///  - Giriş yapılınca (AccountService hazır olunca) bulutla senkronlanır ve oyun profili PlayerId olur.
///  - Her yerel kayıttan ~2 sn sonra değişen veri buluta yazılır (art arda kayıtlar birleştirilir).
///  - Yerel kayıt, buluta gönderilemediyse "kirli" işaretlenir (PlayerPrefs); sonraki girişte önce o gönderilir.
///
/// Çakışma kuralı (basit): bu cihazda gönderilmemiş değişiklik varsa yerel kazanır, yoksa bulut kazanır.
/// İki cihaz da çevrimdışıyken ayrı ayrı ilerlerse en son giriş yapan cihazın verisi kalır.
///
/// Sahneye eklemek gerekmez; oyun açılırken kendini oluşturur.
/// </summary>
public static class CloudSaveSync
{
    public const string InventoryKey = "inventory";
    public const string DecksKey = "decks";

    private const string DirtyPrefix = "cloudsave_dirty_";
    private const string LegacyDoneKey = "cloudsave_legacy_done";
    private const float FlushDelaySeconds = 2f;

    /// <summary>Senkronu tamamlanmış hesabın PlayerId'si. Boşsa bulut kaydı aktif değil.</summary>
    public static string SyncedPlayerId { get; private set; } = string.Empty;
    public static bool IsActive => !string.IsNullOrEmpty(SyncedPlayerId);

    private static bool suppress;          // bulundan inen veriyi yerele yazarken geri göndermemek için
    private static bool syncing;
    private static bool flushScheduled;
    private static bool flushing;
    private static readonly Dictionary<string, object> pending = new Dictionary<string, object>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SyncedPlayerId = string.Empty;
        suppress = syncing = flushScheduled = flushing = false;
        pending.Clear();
    }

    // ------------------------------------------------------------------
    #region Yerel kayıt kancası (SaveSystem çağırır)

    /// <summary>Yerel kayıt yazıldıktan sonra çağrılır. Yalnızca bulut hesabının profili için buluta kuyruğa alır.</summary>
    internal static void OnLocalSaved(string profileId, string key, string json)
    {
        if (suppress || !IsActive || profileId != SyncedPlayerId)
            return;

        SetDirty(profileId, key, true);
        pending[key] = json;
        ScheduleFlush();
    }

    private static async void ScheduleFlush()
    {
        if (flushScheduled)
            return;

        flushScheduled = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(FlushDelaySeconds));
        }
        finally
        {
            flushScheduled = false;
        }

        await FlushAsync();
    }

    /// <summary>Bekleyen değişiklikleri hemen buluta yazar (uygulama arka plana giderken de çağrılır).</summary>
    public static async Task FlushAsync()
    {
        if (flushing || pending.Count == 0)
            return;

        flushing = true;
        try
        {
            while (pending.Count > 0 && IsActive)
            {
                string pid = SyncedPlayerId;
                AccountService account = AccountService.Instance;
                if (account == null || account.PlayerId != pid)
                    break; // hesap değişti / oturum kapalı: bu veriyi başka hesaba yazma

                var batch = new Dictionary<string, object>(pending);
                pending.Clear();

                try
                {
                    await CloudSaveService.Instance.Data.Player.SaveAsync(batch);
                    foreach (string key in batch.Keys)
                        SetDirty(pid, key, false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CloudSave] Buluta yazılamadı, sonra tekrar denenecek: " + e.Message);
                    foreach (var kv in batch)
                        if (!pending.ContainsKey(kv.Key))
                            pending[kv.Key] = kv.Value;
                    break;
                }
            }
        }
        finally
        {
            flushing = false;
        }
    }

    #endregion
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    #region Senkron (giriş sonrası)

    /// <summary>Giriş yapılmış hesabın verisini bulutla eşitler ve oyun profilini PlayerId yapar.</summary>
    public static async Task<bool> SyncAsync()
    {
        if (syncing)
            return false;

        syncing = true;
        try
        {
            if (!await AccountService.EnsureReadyAsync())
                return false;

            string pid = AccountService.Instance.PlayerId;
            if (string.IsNullOrEmpty(pid))
                return false;

            // Eşitleme bitene kadar bu hesaba yazma yapma.
            SyncedPlayerId = string.Empty;
            pending.Clear();

            // PlayerInventory sahnede hazır olana kadar bekle (en çok ~10 sn).
            for (int i = 0; i < 100 && PlayerInventory.Instance == null; i++)
                await Task.Delay(100);

            string legacyProfile = PlayerInventory.CurrentProfileIdStatic;

            Dictionary<string, Item> cloud;
            try
            {
                cloud = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { InventoryKey, DecksKey });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CloudSave] Bulut okunamadı (profil değiştirilmedi): " + e.Message);
                return false;
            }

            // Okuma sürerken hesap değiştiyse bu sonucu kullanma.
            if (AccountService.Instance == null || AccountService.Instance.PlayerId != pid)
                return false;

            bool legacyAllowed = PlayerPrefs.GetInt(LegacyDoneKey, 0) == 0 && legacyProfile != pid;
            var toPush = new Dictionary<string, object>();

            // --- Envanter ---
            string cloudInv = GetCloudString(cloud, InventoryKey);
            bool localInv = SaveSystem.InventoryExists(pid);

            if (IsDirty(pid, InventoryKey) && localInv)
            {
                var local = SaveSystem.LoadInventory(pid);
                bool localEmptyButCloudHas = (local.cards == null || local.cards.Count == 0) && !string.IsNullOrEmpty(cloudInv);
                if (!localEmptyButCloudHas)
                    toPush[InventoryKey] = JsonUtility.ToJson(local, false);
                else
                    ApplyCloudInventory(pid, cloudInv);
            }
            else if (!string.IsNullOrEmpty(cloudInv))
            {
                ApplyCloudInventory(pid, cloudInv);
            }
            else if (localInv)
            {
                toPush[InventoryKey] = JsonUtility.ToJson(SaveSystem.LoadInventory(pid), false);
            }
            else if (legacyAllowed && SaveSystem.InventoryExists(legacyProfile))
            {
                // Bu cihazda bulut öncesi oynanmış kayıt var: ilk hesaba bir kez taşı.
                var legacy = SaveSystem.LoadInventory(legacyProfile);
                WriteLocalInventory(pid, legacy);
                toPush[InventoryKey] = JsonUtility.ToJson(legacy, false);
                Debug.Log("[CloudSave] Yerel envanter (" + legacyProfile + ") bu hesaba taşındı.");
            }

            // --- Desteler ---
            string cloudDecks = GetCloudString(cloud, DecksKey);
            bool localDecks = SaveSystem.DecksExists(pid);

            if (IsDirty(pid, DecksKey) && localDecks)
            {
                toPush[DecksKey] = JsonUtility.ToJson(SaveSystem.LoadDecks(pid), false);
            }
            else if (!string.IsNullOrEmpty(cloudDecks))
            {
                ApplyCloudDecks(pid, cloudDecks);
            }
            else if (localDecks)
            {
                toPush[DecksKey] = JsonUtility.ToJson(SaveSystem.LoadDecks(pid), false);
            }
            else if (legacyAllowed && SaveSystem.DecksExists(legacyProfile))
            {
                var legacy = SaveSystem.LoadDecks(legacyProfile);
                WriteLocalDecks(pid, legacy);
                toPush[DecksKey] = JsonUtility.ToJson(legacy, false);
            }

            // --- Gerekirse buluta yaz ---
            if (toPush.Count > 0)
            {
                try
                {
                    await CloudSaveService.Instance.Data.Player.SaveAsync(toPush);
                    foreach (string key in toPush.Keys)
                        SetDirty(pid, key, false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CloudSave] İlk yükleme başarısız, sonra tekrar denenecek: " + e.Message);
                    foreach (string key in toPush.Keys)
                        SetDirty(pid, key, true);
                }
            }

            PlayerPrefs.SetInt(LegacyDoneKey, 1);
            PlayerPrefs.Save();

            // Bu sırada hesap değiştiyse etkinleştirme.
            if (AccountService.Instance == null || AccountService.Instance.PlayerId != pid)
                return false;

            SyncedPlayerId = pid;

            // Oyuna uygula: profil = PlayerId.
            var inv = PlayerInventory.Instance;
            if (inv != null)
                inv.LoadFromDisk(pid);
            else
                Debug.LogWarning("[CloudSave] PlayerInventory bulunamadı; kayıt yazıldı ama oyuna yüklenemedi.");

            var decks = DeckManagerObject.Instance;
            if (decks != null)
                decks.LoadDecks();

            Debug.Log("[CloudSave] Senkron tamam. PlayerId=" + pid);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Senkron hatası: " + e.Message);
            return false;
        }
        finally
        {
            syncing = false;
        }
    }

    /// <summary>Hesap değişince çağrılır: eski hesaba yazmayı durdurur.</summary>
    public static void Deactivate()
    {
        SyncedPlayerId = string.Empty;
        pending.Clear();
    }

    private static string GetCloudString(Dictionary<string, Item> cloud, string key)
    {
        if (cloud != null && cloud.TryGetValue(key, out Item item) && item != null && item.Value != null)
            return item.Value.GetAsString();
        return null;
    }

    private static void ApplyCloudInventory(string pid, string json)
    {
        try
        {
            var data = JsonUtility.FromJson<InventorySaveData>(json);
            if (data != null)
                WriteLocalInventory(pid, data);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Buluttaki envanter okunamadı: " + e.Message);
        }
    }

    private static void ApplyCloudDecks(string pid, string json)
    {
        try
        {
            var data = JsonUtility.FromJson<DeckSaveData>(json);
            if (data != null)
                WriteLocalDecks(pid, data);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Buluttaki desteler okunamadı: " + e.Message);
        }
    }

    // Yerel dosya bu cihazın anahtarıyla yeniden imzalanır (SaveIntegrity cihaza bağlı).
    private static void WriteLocalInventory(string pid, InventorySaveData data)
    {
        suppress = true;
        try { SaveSystem.SaveInventory(pid, data); }
        finally { suppress = false; }
    }

    private static void WriteLocalDecks(string pid, DeckSaveData data)
    {
        suppress = true;
        try { SaveSystem.SaveDecks(pid, data); }
        finally { suppress = false; }
    }

    private static bool IsDirty(string pid, string key) => PlayerPrefs.GetInt(DirtyPrefix + pid + "_" + key, 0) == 1;

    private static void SetDirty(string pid, string key, bool dirty)
    {
        string prefKey = DirtyPrefix + pid + "_" + key;
        if (dirty)
            PlayerPrefs.SetInt(prefKey, 1);
        else
            PlayerPrefs.DeleteKey(prefKey);

        PlayerPrefs.Save();
    }

    #endregion
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    #region Otomatik başlatma

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("CloudSaveSync");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<CloudSaveSyncRunner>();
    }

    private sealed class CloudSaveSyncRunner : MonoBehaviour
    {
        private AccountService subscribed;

        private async void Start()
        {
            Task<bool> ready = AccountService.EnsureReadyAsync(); // AccountService yoksa oluşturur
            Subscribe();
            await ready;
            RunSync();
        }

        private void Subscribe()
        {
            if (subscribed == null && AccountService.Instance != null)
            {
                subscribed = AccountService.Instance;
                subscribed.StateChanged += OnAccountChanged;
            }
        }

        private void OnDestroy()
        {
            if (subscribed != null)
                subscribed.StateChanged -= OnAccountChanged;
        }

        private void OnAccountChanged()
        {
            AccountService account = AccountService.Instance;
            if (account == null)
                return;

            if (account.State != AccountService.AccountState.Ready)
            {
                Deactivate(); // yeni kimlik açılırken eski hesaba yazma
                return;
            }

            RunSync();
        }

        private async void RunSync()
        {
            // Hesap değiştiyse (ör. kullanıcı adıyla giriş) yeni hesap için tekrar eşitle.
            while (AccountService.Instance != null &&
                   AccountService.Instance.State == AccountService.AccountState.Ready &&
                   AccountService.Instance.PlayerId != SyncedPlayerId)
            {
                if (!await SyncAsync())
                    break;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                _ = FlushAsync(); // mobilde uygulama kapanmadan önce bekleyenleri gönder
        }

        private void OnApplicationFocus(bool focus)
        {
            // Ağ yoktuysa ve eşitleme yapılamadıysa geri dönünce tekrar dene.
            if (focus && !IsActive)
                Retry();
        }

        private async void Retry()
        {
            await AccountService.EnsureReadyAsync(); // başarısız olmuşsa yeniden dener
            Subscribe();
            RunSync();
        }
    }

    #endregion
    // ------------------------------------------------------------------
}
