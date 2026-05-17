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
    public int def;

    public string ability;
    public string passive;

    public string prefab;

    public long iat;
    public long exp;

    public string jti;
}

public static class JwtCardVerifier
{
    public static bool TryParse(string qrText, out CardPayload payload, out string error)
    {
        payload = null;
        error = null;

        if (string.IsNullOrWhiteSpace(qrText))
        {
            error = "QR metni boş.";
            return false;
        }

        // TAB format desteği:
        // id    name    rarity    hp    str    def    ability    passive    prefab
        if (qrText.Contains("\t"))
        {
            return TryParseTabFormat(qrText, out payload, out error);
        }

        // JWT format desteği
        return TryParseJwt(qrText, out payload, out error);
    }

    private static bool TryParseTabFormat(string text, out CardPayload payload, out string error)
    {
        payload = null;
        error = null;

        string[] parts = text.Trim()
    .Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 9)
        {
            parts = text.Trim()
                .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        if (!int.TryParse(parts[3], out int hp))
        {
            error = "HP sayıya çevrilemedi: " + parts[3];
            return false;
        }

        if (!int.TryParse(parts[4], out int str))
        {
            error = "STR sayıya çevrilemedi: " + parts[4];
            return false;
        }

        if (!int.TryParse(parts[5], out int def))
        {
            error = "DEF sayıya çevrilemedi: " + parts[5];
            return false;
        }

        payload = new CardPayload
        {
            id = parts[0],
            name = parts[1],
            rarity = parts[2],

            hp = hp,
            str = str,
            def = def,

            ability = parts[6],
            passive = parts[7],
            prefab = parts[8],

            typ = "CARD",
            ver = 1
        };

        return true;
    }

    private static bool TryParseJwt(string jwt, out CardPayload payload, out string error)
    {
        payload = null;
        error = null;

        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            error = "JWT biçimi hatalı veya TAB format değil.";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            Debug.Log("[JWT JSON] " + json);

            payload = JsonUtility.FromJson<CardPayload>(json);
            Debug.Log($"[JWT PAYLOAD] HP:{payload.hp} STR:{payload.str} DEF:{payload.def}");

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
        if (string.IsNullOrEmpty(input))
            return null;

        string s = input.Replace('-', '+').Replace('_', '/');

        switch (s.Length % 4)
        {
            case 2:
                s += "==";
                break;
            case 3:
                s += "=";
                break;
        }

        return Convert.FromBase64String(s);
    }
}