using UnityEngine;
using TMPro;
using System;
using System.Reflection;

public class SeasonHUDValues : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text pointsValue;
    public TMP_Text streakValue;
    public TMP_Text timeLeftValue;

    [Header("Refresh")]
    [Tooltip("Saniyede kaç kez güncellensin")]
    public float updatesPerSecond = 2f;

    float _timer;

    void OnEnable()
    {
        _timer = 0f;
        RefreshNow();
    }

    void Update()
    {
        if (updatesPerSecond <= 0f) return;
        _timer += Time.deltaTime;
        if (_timer >= 1f / updatesPerSecond)
        {
            _timer = 0f;
            RefreshNow();
        }
    }

    public void RefreshNow()
    {
        // Muhtemel tip adlarýyla bir yönetici/servis bul
        var admin = FindByTypeName(new[] { "SeasonAdminGO", "SeasonAdmin", "SeasonAdminManager" });
        if (admin != null && TryFromObject(admin)) return;

        var mgr = FindByTypeName(new[] { "SeasonManager", "SeasonSystem", "SeasonService" });
        if (mgr != null && TryFromObject(mgr)) return;

        // Bulunamazsa placeholders
        Set(pointsValue, "0");
        Set(streakValue, "0");
        Set(timeLeftValue, "--:--");
    }

    // --------- REFLECTION ---------

    bool TryFromObject(object src)
    {
        var t = src.GetType();

        // Aday adlarý sýrayla denenecek; bulunduðunda tek bir MemberInfo döndürür
        var pts = GetMember(t, "Points", "Pts");
        var sw = GetMember(t, "Streak", "StreakW", "WinStreak");
        var tl = GetMember(t, "TimeLeft", "Remaining", "RemainTime", "RemainingSeconds");

        bool ok = false;
        if (pts != null) { Set(pointsValue, SafeToString(GetValue(src, pts))); ok = true; }
        if (sw != null) { Set(streakValue, SafeToString(GetValue(src, sw))); ok = true; }
        if (tl != null) { Set(timeLeftValue, FormatTime(GetValue(src, tl))); ok = true; }
        return ok;
    }

    // Tip içinde önce property, yoksa field arar; yoksa null
    MemberInfo GetMember(Type t, params string[] candidateNames)
    {
        foreach (var n in candidateNames)
        {
            var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p;
            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f;
        }
        return null;
    }

    object GetValue(object obj, MemberInfo m)
    {
        if (m is FieldInfo f) return f.GetValue(obj);
        if (m is PropertyInfo p) return p.GetValue(obj);
        return null;
    }

    string SafeToString(object v) => v == null ? "0" : v.ToString();

    string FormatTime(object v)
    {
        if (v == null) return "--:--";
        if (v is TimeSpan ts) return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        if (float.TryParse(v.ToString(), out var s)) return $"{(int)(s / 60):00}:{(int)(s % 60):00}";
        return v.ToString();
    }

    void Set(TMP_Text t, string s) { if (t) t.text = s; }

    // “Type yok” sorununu atlatmak için adla bul
    MonoBehaviour FindByTypeName(string[] possibleTypeNames)
    {
        var all = GameObject.FindObjectsOfType<MonoBehaviour>(true);
        foreach (var mb in all)
        {
            var name = mb.GetType().Name;
            for (int i = 0; i < possibleTypeNames.Length; i++)
                if (string.Equals(name, possibleTypeNames[i], StringComparison.OrdinalIgnoreCase))
                    return mb;
        }
        return null;
    }
}
