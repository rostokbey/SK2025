using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class SeasonStandingsSimpleView : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text listText;
    [TextArea] public string emptyText = "Season Standing";

    void OnEnable()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        var lines = FetchTop10Lines();
        if (listText != null)
            listText.text = lines.Count > 0 ? string.Join("\n", lines) : emptyText;
    }

    List<string> FetchTop10Lines()
    {
        // 1) Bir “provider” yakalamaya çalýþ (adý projene göre deðiþebilir)
        var provider = TryBindToProvider();
        if (provider != null)
        {
            // a) Top10 isim/puan koleksiyonu varsa
            var topField = provider.GetType().GetField("Top10") ??
                           provider.GetType().GetProperty("Top10") as MemberInfo;
            if (topField != null)
            {
                var val = GetValue(provider, topField);
                if (val is IEnumerable enumerable)
                {
                    var lines = new List<string>();
                    int i = 1;
                    foreach (var e in enumerable)
                    {
                        // entry içinden Name / Points yakalamaya çalýþ
                        var et = e.GetType();
                        var name = (GetValue(e, et.GetField("name") ?? (MemberInfo)et.GetProperty("name")) ??
                                    GetValue(e, et.GetField("Name") ?? (MemberInfo)et.GetProperty("Name")))?.ToString() ?? $"Player{i}";
                        var pts = (GetValue(e, et.GetField("points") ?? (MemberInfo)et.GetProperty("points")) ??
                                    GetValue(e, et.GetField("Points") ?? (MemberInfo)et.GetProperty("Points")))?.ToString() ?? "0";
                        lines.Add($"{i}. {name} - {pts}");
                        i++;
                        if (i > 10) break;
                    }
                    return lines;
                }
            }

            // b) Yoksa “GetTop10()” gibi bir metot var mý?
            var m = provider.GetType().GetMethod("GetTop10", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var res = m.Invoke(provider, null) as IEnumerable;
                if (res != null)
                {
                    var lines = new List<string>();
                    int i = 1;
                    foreach (var e in res)
                    {
                        lines.Add($"{i}. {e}");
                        i++;
                        if (i > 10) break;
                    }
                    return lines;
                }
            }
        }

        // 2) Placeholder (gerekirse geçici olarak gör)
        return new List<string> {
            "1. PlayerA - 1200",
            "2. PlayerB - 1180",
            "3. PlayerC - 1100",
            "4. PlayerD - 1090",
            "5. PlayerE - 1070",
            "6. PlayerF - 1040",
            "7. PlayerG - 1010",
            "8. PlayerH - 980",
            "9. PlayerI - 960",
            "10. PlayerJ - 940"
        };
    }

    object TryBindToProvider()
    {
        // Tip adlarý: projene göre ekleyebilirsin
        string[] names = { "SeasonStandingsUI", "SeasonStandingsManager", "SeasonRanking", "SeasonService" };
        var all = GameObject.FindObjectsOfType<MonoBehaviour>(true);
        foreach (var mb in all)
        {
            foreach (var n in names)
                if (string.Equals(mb.GetType().Name, n, StringComparison.OrdinalIgnoreCase))
                    return mb;
        }
        return null;
    }

    object GetValue(object obj, MemberInfo m)
    {
        if (m == null) return null;
        if (m is FieldInfo f) return f.GetValue(obj);
        if (m is PropertyInfo p) return p.GetValue(obj);
        return null;
    }
}
