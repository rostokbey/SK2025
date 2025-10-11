using System;
using System.Text;
using UnityEngine;

[Serializable]
public class CardPayload
{
    public string typ;
    public int ver;

    public string id;
    public string name;
    public string rarity;

    public int hp;
    public int str;
    public int dex;

    public string ability;
    public string passive;

    // Resources köküne göre prefab yolu (ör: "Prefabs3D/AgirZirh_insan3D")
    public string prefab;

    public long iat;   // opsiyonel
    public long exp;   // opsiyonel
}

public static class JwtCardVerifier
{
    // JWT’yi imza KONTROLSÜZ şekilde çözüp payload’u döndürür.
    public static bool TryParse(string jwt, out CardPayload payload, out string error)
    {
        payload = null;
        error = null;

        if (string.IsNullOrEmpty(jwt)) { error = "Boş JWT"; return false; }

        var parts = jwt.Split('.');
        if (parts.Length < 2) { error = "JWT biçimi hatalı"; return false; }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            payload = JsonUtility.FromJson<CardPayload>(json);

            if (payload == null || string.IsNullOrEmpty(payload.id))
            {
                error = "Payload çözümlenemedi (id yok).";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "Payload decode hatası: " + ex.Message;
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        string s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
