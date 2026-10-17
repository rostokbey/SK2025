using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Unity Services başlatma ve oyuncu oturumu için TEK giriş noktası.
///
/// - Sahneye eklemen gerekmez; oyun açılırken kendini oluşturur (BeforeSceneLoad).
/// - Birden fazla script aynı anda EnsureReadyAsync() çağırsa bile başlatma ve giriş bir kez yapılır.
/// - Açılışta anonim giriş yapar; oyuncu isterse aynı oyuncuya kullanıcı adı + şifre bağlayabilir
///   (uygulama silinse bile aynı hesaba geri dönülebilsin diye).
/// - Canlıya geçişte sağlayıcı (ör. Unity Player Accounts) yalnızca bu dosyada değişir.
///
/// ÖN KOŞUL: Kullanıcı adı/şifre için Unity panelinde (Services > Authentication > Configure)
/// "Username &amp; Password" kimlik sağlayıcısı eklenmiş olmalı.
/// </summary>
public class AccountService : MonoBehaviour
{
    public enum AccountState { NotStarted, Starting, Ready, Failed }

    public sealed class AccountResult
    {
        public bool Success { get; }
        public string Message { get; }

        public AccountResult(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
        }

        public static AccountResult Ok(string message = "") => new AccountResult(true, message);
        public static AccountResult Fail(string message) => new AccountResult(false, message);
    }

    public static AccountService Instance { get; private set; }

    /// <summary>
    /// Aynı bilgisayarda birden fazla örnek açıyorsan (Editor + build) her birine farklı profil ver,
    /// yoksa hepsi aynı anonim oyuncuyu paylaşır. Komut satırından da verilebilir: -profile ad
    /// </summary>
    public static string ProfileOverride;

    public AccountState State { get; private set; } = AccountState.NotStarted;
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Oyuncuya bağlı kullanıcı adı. Boşsa kullanıcı adı bağlanmamış (yalnızca anonim).</summary>
    public string Username { get; private set; } = string.Empty;
    public bool HasLinkedUsername => !string.IsNullOrEmpty(Username);

    public event Action StateChanged;

    public bool IsSignedIn =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : string.Empty;

    private Task<bool> startTask;
    private bool busy; // bağlama / giriş / kimlik sıfırlama sürerken true

    // ------------------------------------------------------------------
    #region Kendini oluşturma

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // "Enter Play Mode Options" ile domain reload kapalıysa eski örnek kalmasın.
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        var go = new GameObject("AccountService");
        DontDestroyOnLoad(go);
        go.AddComponent<AccountService>(); // Awake, Instance'ı atar

        // Test paneli yalnızca Editor ve Development Build'lerde eklenir.
        if (Debug.isDebugBuild)
            go.AddComponent<AccountDebugPanel>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    #endregion
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    #region Başlatma ve giriş

    /// <summary>
    /// Unity Services başlatılmış ve oyuncu giriş yapmış olana kadar bekler.
    /// Relay, Cloud Save vb. çağrılardan ÖNCE await edilmelidir. Başarısızsa false döner,
    /// ayrıntı LastError içindedir; sonraki çağrıda yeniden denenir.
    /// </summary>
    public static Task<bool> EnsureReadyAsync()
    {
        if (Instance == null)
            Bootstrap();

        return Instance.StartAsync();
    }

    public async Task<bool> StartAsync()
    {
        while (busy)
            await Task.Yield(); // hesap işlemi sürerken (ör. hesap değiştirme) araya girme

        return await StartCoreAsync();
    }

    private Task<bool> StartCoreAsync()
    {
        if (State == AccountState.Ready && IsSignedIn)
            return Task.FromResult(true);

        if (startTask != null && !startTask.IsCompleted)
            return startTask;

        startTask = RunStartAsync();
        return startTask;
    }

    private async Task<bool> RunStartAsync()
    {
        SetState(AccountState.Starting, string.Empty);

        try
        {
            await EnsureServicesInitializedAsync();
            await EnsureSignedInAsync();
            await RefreshAccountInfoAsync();

            SetState(AccountState.Ready, string.Empty);
            Debug.Log($"[Account] Hazır. PlayerId={PlayerId}, kullanıcı adı={(HasLinkedUsername ? Username : "(yok)")}");
            return true;
        }
        catch (AuthenticationException e)
        {
            return FailStart($"Giriş hatası ({e.ErrorCode}): {e.Message}");
        }
        catch (RequestFailedException e)
        {
            return FailStart($"Servis hatası ({e.ErrorCode}): {e.Message}");
        }
        catch (Exception e)
        {
            return FailStart(e.Message);
        }
    }

    private bool FailStart(string message)
    {
        Debug.LogError($"[Account] {message}");
        SetState(AccountState.Failed, message);
        return false;
    }

    private static async Task EnsureServicesInitializedAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
            return;

        // Başka bir script başlatmayı zaten başlattıysa bitmesini bekle.
        while (UnityServices.State == ServicesInitializationState.Initializing)
            await Task.Yield();

        if (UnityServices.State == ServicesInitializationState.Initialized)
            return;

        var options = new InitializationOptions();
        string profile = ResolveProfile();
        if (!string.IsNullOrEmpty(profile))
            options.SetProfile(profile);

        await UnityServices.InitializeAsync(options);
    }

    private static async Task EnsureSignedInAsync()
    {
        var auth = AuthenticationService.Instance;
        if (auth.IsSignedIn)
            return;

        try
        {
            await auth.SignInAnonymouslyAsync();
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.InvalidSessionToken)
        {
            // Kayıtlı oturum geçersiz (hesap silinmiş / token bozuk): temizleyip yeni anonim oyuncu aç.
            Debug.LogWarning("[Account] Kayıtlı oturum geçersiz; temizlenip yeniden giriş yapılıyor.");
            auth.ClearSessionToken();
            await auth.SignInAnonymouslyAsync();
        }
    }

    private async Task RefreshAccountInfoAsync()
    {
        try
        {
            PlayerInfo info = await AuthenticationService.Instance.GetPlayerInfoAsync();
            Username = info != null && info.Username != null ? info.Username : string.Empty;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Account] Oyuncu bilgisi alınamadı: {e.Message}");
            Username = string.Empty;
        }
    }

    private static string ResolveProfile()
    {
        string raw = ProfileOverride;

        if (string.IsNullOrEmpty(raw))
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-profile", StringComparison.OrdinalIgnoreCase))
                {
                    raw = args[i + 1];
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        // SDK yalnızca harf, rakam, '-' ve '_' kabul eder (en çok 30 karakter).
        var sb = new StringBuilder();
        foreach (char c in raw)
        {
            bool allowed = (c < 128 && char.IsLetterOrDigit(c)) || c == '-' || c == '_';
            if (allowed && sb.Length < 30)
                sb.Append(c);
        }

        return sb.ToString();
    }

    #endregion
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    #region Kullanıcı adı + şifre

    /// <summary>
    /// Şu anki (anonim) oyuncuya kullanıcı adı ve şifre ekler. Oyuncu kimliği (PlayerId) DEĞİŞMEZ,
    /// bu yüzden o kimliğe bağlı tüm veri korunur.
    /// </summary>
    public async Task<AccountResult> LinkUsernamePasswordAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return AccountResult.Fail("Kullanıcı adı ve şifre gerekli.");

        if (busy)
            return AccountResult.Fail("Başka bir hesap işlemi sürüyor.");

        busy = true;
        try
        {
            if (!await StartCoreAsync())
                return AccountResult.Fail($"Servis hazır değil: {LastError}");

            await AuthenticationService.Instance.AddUsernamePasswordAsync(username.Trim(), password);
            await RefreshAccountInfoAsync();
            RaiseChanged();
            return AccountResult.Ok("Kullanıcı adı ve şifre bu oyuncuya bağlandı.");
        }
        catch (Exception e)
        {
            return AccountResult.Fail(Describe(e));
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>
    /// Daha önce bağlanmış bir hesaba giriş yapar (ör. uygulamayı silip yeniden kurduktan sonra).
    /// Şu anki anonim oyuncu terk edilir. Giriş başarısız olursa önceki oturum geri açılır.
    /// </summary>
    public async Task<AccountResult> SignInUsernamePasswordAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return AccountResult.Fail("Kullanıcı adı ve şifre gerekli.");

        if (busy)
            return AccountResult.Fail("Başka bir hesap işlemi sürüyor.");

        busy = true;
        try
        {
            if (!await StartCoreAsync())
                return AccountResult.Fail($"Servis hazır değil: {LastError}");

            var auth = AuthenticationService.Instance;

            // Oturum açıkken yeniden giriş yapılamaz. Kayıtlı oturum bilgisini silmeden çık ki
            // giriş başarısız olursa eski oyuncuya geri dönebilelim.
            auth.SignOut(false);

            try
            {
                await auth.SignInWithUsernamePasswordAsync(username.Trim(), password);
            }
            catch
            {
                await RestorePreviousSessionAsync();
                throw;
            }

            await RefreshAccountInfoAsync();
            SetState(AccountState.Ready, string.Empty);
            return AccountResult.Ok("Giriş yapıldı. Önceki anonim oyuncu terk edildi.");
        }
        catch (Exception e)
        {
            return AccountResult.Fail(Describe(e));
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>
    /// TEST: Kayıtlı oturumu siler ve yepyeni bir anonim oyuncu açar (temiz hesapla denemek için).
    /// Eski oyuncuya yalnızca kullanıcı adı bağlıysa geri dönülebilir.
    /// </summary>
    public async Task<AccountResult> CreateNewTestIdentityAsync()
    {
        if (busy)
            return AccountResult.Fail("Başka bir hesap işlemi sürüyor.");

        busy = true;
        try
        {
            if (UnityServices.State == ServicesInitializationState.Initialized &&
                AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut(true); // true: kayıtlı oturumu da sil
            }
            else if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                AuthenticationService.Instance.ClearSessionToken();
            }

            Username = string.Empty;
            startTask = null;
            SetState(AccountState.NotStarted, string.Empty);

            if (!await StartCoreAsync())
                return AccountResult.Fail($"Servis hazır değil: {LastError}");

            return AccountResult.Ok($"Yeni test kimliği açıldı: {PlayerId}");
        }
        catch (Exception e)
        {
            return AccountResult.Fail(Describe(e));
        }
        finally
        {
            busy = false;
        }
    }

    private static async Task RestorePreviousSessionAsync()
    {
        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Account] Önceki oturum geri açılamadı: {e.Message}");
        }
    }

    private static string Describe(Exception e)
    {
        if (e is AuthenticationException auth)
        {
            if (auth.ErrorCode == AuthenticationErrorCodes.ClientInvalidUserState)
                return "Oturum durumu bu işleme uygun değil.";

            return $"{auth.Message} (kod {auth.ErrorCode})";
        }

        if (e is RequestFailedException request)
            return $"{request.Message} (kod {request.ErrorCode})";

        return e.Message;
    }

    #endregion
    // ------------------------------------------------------------------

    private void SetState(AccountState state, string error)
    {
        State = state;
        LastError = error ?? string.Empty;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        StateChanged?.Invoke();
    }
}
