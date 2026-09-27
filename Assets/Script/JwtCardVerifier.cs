using System;
using System.Security.Cryptography;
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

[Serializable]
internal class JwtHeader
{
    public string alg;
    public string typ;
}

/// <summary>
/// Fiziksel kartların üzerindeki QR kodlarını doğrular.
///
/// GÜVENLİK NOTU (önceki sürümle karşılaştırma):
/// Önceki uygulama JWT'nin sadece payload kısmını base64-decode edip
/// okuyordu; imza (üçüncü parça) HİÇ doğrulanmıyordu. Bu, "JwtCardVerifier"
/// adına rağmen aslında bir doğrulama yapmadığı, formatı doğru olan
/// HERHANGİ bir metnin (ör. elle üretilmiş sahte bir QR) geçerli bir kart
/// gibi kabul edilebildiği anlamına geliyordu (istediğiniz HP/STR/DEF/rarity
/// değerleriyle).
///
/// Bu sürüm RS256 (RSA + SHA-256) imza doğrulaması yapar:
///   - İmzayı yalnızca özel anahtarı (private key) elinde bulunduran kart
///     üretim aracı oluşturabilir (bkz. Assets/Script/Editor/CardSigningTool.cs).
///   - Oyun istemcisi sadece ilgili AÇIK anahtarı (public key) içerir; bu
///     anahtar "gizli" olmak zorunda değildir, sadece imza üretmek için
///     kullanılamaz, yalnızca doğrulamak için kullanılabilir. Bu yüzden
///     HMAC (paylaşılan gizli anahtar) yerine RSA (asimetrik) tercih
///     edildi: HMAC seçilseydi, istemciye gömülen gizli anahtar tersine
///     mühendislikle çıkarılıp sahte kart imzalamak için kullanılabilirdi.
///   - "alg" alanı da doğrulanır (yalnızca "RS256" kabul edilir) ki bir
///     saldırgan "alg: none" gibi bir başlıkla imza kontrolünü atlatmaya
///     çalışamasın (klasik "algorithm confusion" saldırısı).
///   - "exp" (son kullanma) alanı doluysa süresi geçmiş kartlar reddedilir.
/// </summary>
public static class JwtCardVerifier
{
    // === AÇIK ANAHTAR (PUBLIC KEY) ===
    // Bu, RSA açık anahtarının RsaKeyPortability formatında (bkz.
    // Assets/Script/System/RsaKeyPortability.cs) Base64 ile kodlanmış hâlidir
    // - standart bir PEM/DER değildir, CardSigningTool ile aynı basit JSON
    // zarfını kullanır (Unity'nin bazı platformlarda desteklemediği
    // ExportSubjectPublicKeyInfo/ImportSubjectPublicKeyInfo API'lerinden
    // kaçınmak için). Karşılığı olan ÖZEL anahtar SADECE kart üretim
    // aracında (sunucu/ofline araç) tutulmalı, bu repoya veya istemciye
    // ASLA eklenmemelidir.
    //
    // AŞAĞIDAKİ ANAHTAR YALNIZCA YER TUTUCUDUR / TEST AMAÇLIDIR.
    // Üretime almadan önce CardSigningTool > "Generate New RSA KeyPair" ile
    // gerçek bir anahtar çifti üretip bu sabiti güncelleyin ve özel anahtarı
    // güvenli şekilde saklayın (repoya koymayın).
    private const string CardSigningPublicKeyBase64 =
        "REPLACE_WITH_REAL_PUBLIC_KEY_FROM_CARD_SIGNING_TOOL";

    private static RSA _cachedPublicKey;

    private static RSA GetPublicKey()
    {
        if (_cachedPublicKey != null)
            return _cachedPublicKey;

        if (string.IsNullOrEmpty(CardSigningPublicKeyBase64) ||
            CardSigningPublicKeyBase64.StartsWith("REPLACE_WITH_"))
        {
            Debug.LogError("[JwtCardVerifier] Açık anahtar ayarlanmamış! " +
                            "CardSigningTool ile bir anahtar çifti üretip " +
                            "CardSigningPublicKeyBase64 alanını güncelleyin. " +
                            "Anahtar ayarlanana kadar TÜM imzalı kartlar reddedilecek.");
            return null;
        }

        try
        {
            var rsa = RSA.Create();
            RsaKeyPortability.ImportPublicKey(rsa, CardSigningPublicKeyBase64);
            _cachedPublicKey = rsa;
            return _cachedPublicKey;
        }
        catch (Exception ex)
        {
            Debug.LogError("[JwtCardVerifier] Açık anahtar okunamadı: " + ex.Message);
            return null;
        }
    }

    public static bool TryParse(string qrText, out CardPayload payload, out string error)
    {
        payload = null;
        error = null;

        if (string.IsNullOrWhiteSpace(qrText))
        {
            error = "QR metni boş.";
            return false;
        }

        // TAB format desteği: yalnızca Editor / Development Build'de.
        // Bu format İMZASIZDIR (kart üretim aracı hızlı test için düz metin
        // üretir), bu yüzden yayın (release) derlemesinde KABUL EDİLMEZ.
        // Aksi halde bu, imza doğrulamasını tamamen atlatan bir arka kapı
        // olurdu.
        if (qrText.Contains("\t"))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return TryParseTabFormat(qrText, out payload, out error);
#else
            error = "İmzasız (TAB) kart formatı yayın sürümünde desteklenmiyor.";
            return false;
#endif
        }

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

        if (parts.Length < 9)
        {
            error = "TAB formatı eksik alan içeriyor.";
            return false;
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
        if (parts.Length != 3)
        {
            error = "JWT biçimi hatalı (header.payload.signature bekleniyor).";
            return false;
        }

        string headerPart = parts[0];
        string payloadPart = parts[1];
        string signaturePart = parts[2];

        // 1) Header'ı çöz ve algoritmayı kontrol et.
        JwtHeader header;
        try
        {
            string headerJson = Encoding.UTF8.GetString(Base64UrlDecode(headerPart));
            header = JsonUtility.FromJson<JwtHeader>(headerJson);
        }
        catch (Exception ex)
        {
            error = "Header decode hatası: " + ex.Message;
            return false;
        }

        if (header == null || !string.Equals(header.alg, "RS256", StringComparison.Ordinal))
        {
            // "none" veya beklenmeyen başka bir algoritma her zaman reddedilir.
            error = "Desteklenmeyen veya eksik imza algoritması: " + (header?.alg ?? "(yok)");
            return false;
        }

        // 2) İmzayı doğrula: RSASSA-PKCS1-v1_5 + SHA-256, "header.payload" üzerinde.
        var publicKey = GetPublicKey();
        if (publicKey == null)
        {
            error = "Doğrulama anahtarı yüklenemedi.";
            return false;
        }

        byte[] signedData = Encoding.ASCII.GetBytes(headerPart + "." + payloadPart);
        byte[] signature;
        try
        {
            signature = Base64UrlDecode(signaturePart);
        }
        catch (Exception ex)
        {
            error = "İmza decode hatası: " + ex.Message;
            return false;
        }

        bool signatureValid;
        try
        {
            signatureValid = publicKey.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            error = "İmza doğrulama hatası: " + ex.Message;
            return false;
        }

        if (!signatureValid)
        {
            error = "Kart imzası geçersiz (sahte veya bozulmuş QR).";
            return false;
        }

        // 3) İmza geçerli -> payload'ı güvenle çöz.
        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlDecode(payloadPart));
            payload = JsonUtility.FromJson<CardPayload>(json);

            if (payload == null || string.IsNullOrEmpty(payload.id))
            {
                error = "Payload çözümlenemedi (id yok).";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = "Payload decode hatası: " + ex.Message;
            return false;
        }

        // 4) Son kullanma tarihi kontrolü (varsa).
        if (payload.exp > 0)
        {
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (nowUnix > payload.exp)
            {
                error = "Kartın süresi dolmuş.";
                payload = null;
                return false;
            }
        }

        return true;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        if (string.IsNullOrEmpty(input))
            return Array.Empty<byte>();

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
