using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tek kapı: Casual/Season bayrağını set eder ve mevcut StartBattleManager (veya benzeri)
/// üzerindeki gerçek başlatma metodunu çağırır. methodName boşsa yaygın isimleri otomatik dener.
/// Inspector ataması yapmadan da derlenir; target yoksa sadece log basar / opsiyonel sahne fallback.
/// </summary>
public class BattleStartAdapter : MonoBehaviour
{
    [Header("Hedef (mevcut başlatıcı)")]
    [Tooltip("StartBattleManager script'inin bağlı olduğu component (boş bırakılabilir; sadece log basar).")]
    public MonoBehaviour target;  // örn: StartBattleManager

    [Header("Çağrılacak metod")]
    [Tooltip("Bilmiyorsanız boş bırakın; otomatik keşif dener (StartBattle/StartMatch/BeginMatch/GoBattleScene/StartGame/Play).")]
    public string methodName = "";

    public enum CallMode { Auto, NoArg, IntTeamSize }
    [Tooltip("Auto: önce int parametreli, sonra argsız dener.")]
    public CallMode callMode = CallMode.Auto;

    [Header("Opsiyonel sahne fallback'i")]
    [Tooltip("Hedef metod bulunamazsa bu sahne yüklenir (boşsa atlanır).")]
    public string fallbackSceneName = "";

    // ------------ BUTONLARIN / DIŞ ÇAĞRILARIN KULLANACAĞI TEK METOT ------------
    public void StartMatch(int teamSize, bool seasonMode)
    {
        // 1) Bağlam bayrakları
        MatchContext.LastMatchWasSeason = seasonMode;
        MatchContext.LastMatchTeamSize = Mathf.Clamp(teamSize, 1, 3);

        // 2) Sezon elenme kapısı
        if (seasonMode && SeasonManager.Instance?.IsEliminated() == true)
        {
            Debug.LogWarning("[BattleStartAdapter] Bu sezon elendiniz (puan=0).");
            return;
        }

        // 3) Gerçek başlatıcıyı çağırmayı dene
        bool invoked = InvokeTarget(MatchContext.LastMatchTeamSize);

        // 4) Olmazsa opsiyonel fallback sahnesi
        if (!invoked && !string.IsNullOrEmpty(fallbackSceneName))
        {
            Debug.Log($"[BattleStartAdapter] Fallback: LoadScene({fallbackSceneName})");
            SceneManager.LoadScene(fallbackSceneName);
            invoked = true;
        }

        if (!invoked)
            Debug.LogError("[BattleStartAdapter] Hedef metod bulunamadı. (target/metodName/callMode kontrol edin)");

        Debug.Log($"[BattleStartAdapter] Start → teamSize={MatchContext.LastMatchTeamSize}, season={MatchContext.LastMatchWasSeason}");
    }

    // -------- Tek parametreli sarmalayıcılar (Unity Button için) --------
    public void StartCasual(int teamSize) => StartMatch(teamSize, false);
    public void StartSeason(int teamSize) => StartMatch(teamSize, true);

    // Parametresiz kısa yollar (istersen kullan)
    public void StartCasual1v1() { StartMatch(1, false); }
    public void StartSeason1v1() { StartMatch(1, true); }
    public void StartSeason2v2() { StartMatch(2, true); }
    public void StartSeason3v3() { StartMatch(3, true); }

    // --------------------------- İç yardımcı ---------------------------
    bool InvokeTarget(int teamSize)
    {
        if (target == null)
        {
            Debug.LogWarning("[BattleStartAdapter] target atanmadı; sadece bağlam set edildi.");
            return false;
        }

        var t = target.GetType();
        string[] candidates = string.IsNullOrWhiteSpace(methodName)
            ? new[] { "StartBattle", "StartMatch", "BeginMatch", "GoBattleScene", "StartGame", "Play" }
            : new[] { methodName };

        try
        {
            if (callMode == CallMode.IntTeamSize || callMode == CallMode.Auto)
            {
                foreach (var name in candidates)
                {
                    var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                        binder: null, types: new Type[] { typeof(int) }, modifiers: null);
                    if (m != null) { m.Invoke(target, new object[] { teamSize }); return true; }
                }
            }

            if (callMode == CallMode.NoArg || callMode == CallMode.Auto)
            {
                foreach (var name in candidates)
                {
                    var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                        binder: null, types: Type.EmptyTypes, modifiers: null);
                    if (m != null) { m.Invoke(target, null); return true; }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BattleStartAdapter] çağrı hatası: {e.Message}");
        }

        return false;
    }
}

/// <summary> Maç bağlamı (maç sonu akışlar burada season/casual ve takım boyutunu okur). </summary>
public static class MatchContext
{
    public static bool LastMatchWasSeason { get; set; }
    public static int LastMatchTeamSize { get; set; }
}
