using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Oyunun giriþ paneli. Kullanýcý adý + þifre ile AccountService'e baðlanýr:
///  - "Kayýt ol"  -> OnRegisterClicked(): þu anki oyuncuya kullanýcý adý ve þifre baðlar (PlayerId deðiþmez,
///                   bu cihazda toplanmýþ karakterler hesaba baðlanýr).
///  - "Giriþ yap" -> OnLoginClicked(): daha önce kayýt olunmuþ hesaba girer; karakterler Cloud Save'den gelir.
///
/// Sahnede yapýlacaklar (bir kerelik):
///  1) Bir TMP_InputField daha ekle (þifre için) ve aþaðýdaki "Password Input" alanýna ata.
///  2) Ýstersen bir TMP_Text (durum mesajý) ata.
///  3) Kayýt ol / Giriþ yap düðmelerinin OnClick olayýna bu bileþendeki OnRegisterClicked / OnLoginClicked'i baðla.
///  4) Giriþten sonra arayüzü yenilemek / sahne deðiþtirmek istersen "On Login Success" olayýný kullan.
/// </summary>
public class LocalLogin : MonoBehaviour
{
    [Header("Giriþ alanlarý")]
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;

    [Header("Opsiyonel")]
    public TMP_Text statusText;
    public Button registerButton;
    public Button loginButton;

    [Header("Olaylar")]
    public UnityEvent onLoginSuccess;

    private bool busy;

    private void Awake()
    {
        if (passwordInput != null)
            passwordInput.contentType = TMP_InputField.ContentType.Password;
    }

    // ------------------------------------------------------------------
    #region Hesap giriþi (düðmelere baðla)

    public async void OnRegisterClicked()
    {
        await RunAccountActionAsync(true);
    }

    public async void OnLoginClicked()
    {
        await RunAccountActionAsync(false);
    }

    private async Task RunAccountActionAsync(bool register)
    {
        if (busy)
            return;

        string user = usernameInput != null ? usernameInput.text.Trim() : string.Empty;
        string pass = passwordInput != null ? passwordInput.text : string.Empty;

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            SetStatus("Kullanýcý adý ve þifre gir.");
            return;
        }

        SetBusy(true);
        SetStatus("Ýþleniyor...");

        try
        {
            if (!await AccountService.EnsureReadyAsync())
            {
                SetStatus("Sunucuya baðlanýlamadý: " + AccountService.Instance.LastError);
                return;
            }

            AccountService.AccountResult result = register
                ? await AccountService.Instance.LinkUsernamePasswordAsync(user, pass)
                : await AccountService.Instance.SignInUsernamePasswordAsync(user, pass);

            if (!result.Success)
            {
                SetStatus(result.Message);
                return;
            }

            SetStatus(register ? "Hesap oluþturuldu. Kaydediliyor..." : "Giriþ yapýldý. Karakterler yükleniyor...");

            bool synced = await WaitForCloudSyncAsync();
            if (passwordInput != null)
                passwordInput.text = string.Empty;

            if (!synced)
            {
                SetStatus("Giriþ yapýldý ama bulut verisi yüklenemedi. Ýnternet baðlantýný kontrol edip tekrar dene.");
                return;
            }

            int count = PlayerInventory.Instance != null ? PlayerInventory.Instance.myCards.Count : 0;
            SetStatus("Hazýr. Karakter sayýsý: " + count);
            onLoginSuccess?.Invoke();
        }
        finally
        {
            SetBusy(false);
        }
    }

    // Cloud Save senkronu (CloudSaveSync) bu hesap için tamamlanana kadar bekler (en çok ~15 sn).
    private static async Task<bool> WaitForCloudSyncAsync()
    {
        for (int i = 0; i < 150; i++)
        {
            AccountService account = AccountService.Instance;
            if (account != null &&
                CloudSaveSync.IsActive &&
                CloudSaveSync.SyncedPlayerId == account.PlayerId)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        if (registerButton != null) registerButton.interactable = !value;
        if (loginButton != null) loginButton.interactable = !value;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log("[LocalLogin] " + message);
    }

    #endregion
    // ------------------------------------------------------------------

    // Eski düðmeler bozulmasýn diye korunan yardýmcýlar.
    // Profil seçildiðinde çaðýrmak için basit yardýmcý
    public void SelectProfileAndLoad()
    {
        string profileId = (usernameInput && !string.IsNullOrWhiteSpace(usernameInput.text))
            ? usernameInput.text.Trim()
            : "DEFAULT";

        // Bulut hesabý aktifse profil her zaman hesabýn PlayerId'sidir (yazýlan ad yok sayýlýr).
        if (CloudSaveSync.IsActive)
            profileId = CloudSaveSync.SyncedPlayerId;

        TryLoadInventory(profileId);
    }

    public void TryLoadInventory(string profileId)
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null)
        {
            Debug.LogError("[LocalLogin] PlayerInventory bulunamadý.");
            return;
        }

        inv.LoadFromDisk(profileId);
        Debug.Log($"[LocalLogin] Envanter yüklendi (profil={profileId}).");

        var deckManager = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>();
        if (deckManager != null)
        {
            deckManager.LoadDecks();
            Debug.Log("[LocalLogin] Envanter sonrasý desteler tekrar yüklendi.");
        }
    }

    public void TrySaveInventory()
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null)
        {
            Debug.LogError("[LocalLogin] PlayerInventory bulunamadý.");
            return;
        }

        string pid = !string.IsNullOrEmpty(inv.CurrentProfileId) ? inv.CurrentProfileId : "DEFAULT";
        inv.SaveToDisk(pid);

        var deckManager = DeckManagerObject.Instance ?? FindObjectOfType<DeckManagerObject>();
        if (deckManager != null)
        {
            deckManager.SaveDecks();
            Debug.Log("[LocalLogin] Desteler kaydedildi.");
        }
        else
        {
            Debug.LogWarning("[LocalLogin] DeckManagerObject bulunamadý, desteler kaydedilemedi.");
        }

        Debug.Log($"[LocalLogin] Envanter kaydedildi. (profil={pid})");
    }
}
