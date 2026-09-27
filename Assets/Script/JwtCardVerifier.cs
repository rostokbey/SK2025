using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

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
/// Bu sürüm ES256 (ECDSA P-256 + SHA-256) imza doğrulaması yapar
/// (Python tarafında PyJWT'nin "ES256" algoritmasıyla, ör. mint_qr_tokens.py
/// ile üretilen kartlarla birebir uyumludur):
///   - İmzayı yalnızca özel anahtarı (ec_private.pem) elinde bulunduran kart
///     üretim script'i oluşturabilir.
///   - Oyun istemcisi sadece ilgili AÇIK anahtarı (public key) içerir; bu
///     anahtar "gizli" olmak zorunda değildir, sadece imza üretmek için
///     kullanılamaz, yalnızca doğrulamak için kullanılabilir. Bu yüzden
///     HMAC (paylaşılan gizli anahtar) yerine ECDSA (asimetrik) tercih
///     edildi: HMAC seçilseydi, istemciye gömülen gizli anahtar tersine
///     mühendislikle çıkarılıp sahte kart imzalamak için kullanılabilirdi.
///     ECDSA P-256, aynı güvenlik seviyesindeki RSA'ya göre çok daha kısa
///     anahtar/imza ürettiği için QR kodunu da küçültüp okumayı
///     kolaylaştırır.
///   - "alg" alanı da doğrulanır (yalnızca "ES256" kabul edilir) ki bir
///     saldırgan "alg: none" gibi bir başlıkla imza kontrolünü atlatmaya
///     çalışamasın (klasik "algorithm confusion" saldırısı).
///   - "exp" (son kullanma) alanı doluysa süresi geçmiş kartlar reddedilir.
/// </summary>
public static class JwtCardVerifier
{
    // === AÇIK ANAHTAR (PUBLIC KEY) ===
    // Bu, EC (P-256) açık anahtarının EcKeyPortability formatında (bkz.
    // Assets/Script/System/EcKeyPortability.cs) Base64 ile kodlanmış hâlidir
    // - standart bir PEM değildir, kendi basit JSON zarfımızdır (Unity'nin
    // bazı platformlarda desteklemediği ImportSubjectPublicKeyInfo gibi
    // .NET 5+ API'lerinden kaçınmak için). ec_private.pem'den bu formatı
    // üretmek için export_ec_public_key.py script'ini kullanın. Karşılığı
    // olan ÖZEL anahtar (ec_private.pem) SADECE kart üretim script'inde
    // tutulmalı, bu repoya veya istemciye ASLA eklenmemelidir.
    //
    // AŞAĞIDAKİ ANAHTAR YALNIZCA YER TUTUCUDUR / TEST AMAÇLIDIR.
    private const string CardSigningPublicKeyBase64 =
    "eyJ4IjoiMGR0UGFUdkdHbGFMZmdseGR2L00wRzJyWiswR3JONWhNN1FHZC92MWdSdz0iLCJ5IjoiaUd2MjRDbHpVVWhOL3l6K3U5SWRrRE53RUJ0TTFjcjY5c1BraVpsZnUvaz0ifQ==";

    [Serializable]
    private class EcPublicBlob
    {
        public string x;
        public string y;
    }

    private static ECPublicKeyParameters _cachedPublicKey;

    private static ECPublicKeyParameters GetPublicKey()
    {
        if (_cachedPublicKey != null)
            return _cachedPublicKey;

        if (string.IsNullOrEmpty(CardSigningPublicKeyBase64) ||
            CardSigningPublicKeyBase64.StartsWith("REPLACE_WITH_"))
        {
            Debug.LogError(
                "[JwtCardVerifier] Açık anahtar ayarlanmamış.");
            return null;
        }

        try
        {
            byte[] jsonBytes =
                Convert.FromBase64String(CardSigningPublicKeyBase64);

            string json = Encoding.UTF8.GetString(jsonBytes);
            EcPublicBlob blob =
                JsonUtility.FromJson<EcPublicBlob>(json);

            if (blob == null ||
                string.IsNullOrEmpty(blob.x) ||
                string.IsNullOrEmpty(blob.y))
            {
                throw new Exception(
                    "Açık anahtar X ve Y koordinatlarını içermiyor.");
            }

            byte[] xBytes = Convert.FromBase64String(blob.x);
            byte[] yBytes = Convert.FromBase64String(blob.y);

            if (xBytes.Length != 32 || yBytes.Length != 32)
            {
                throw new Exception(
                    "P-256 koordinatları 32 bayt olmalıdır.");
            }

            var curve = SecNamedCurves.GetByName("secp256r1");

            if (curve == null)
                throw new Exception("secp256r1 eğrisi bulunamadı.");

            var domain = new ECDomainParameters(
                curve.Curve,
                curve.G,
                curve.N,
                curve.H,
                curve.GetSeed());

            var point = curve.Curve.ValidatePoint(
                new BigInteger(1, xBytes),
                new BigInteger(1, yBytes));

            _cachedPublicKey =
                new ECPublicKeyParameters(point, domain);

            return _cachedPublicKey;
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "[JwtCardVerifier] Açık anahtar okunamadı:\\n" + ex);
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

        if (header == null || !string.Equals(header.alg, "ES256", StringComparison.Ordinal))
        {
            // "none" veya beklenmeyen başka bir algoritma her zaman reddedilir.
            error = "Desteklenmeyen veya eksik imza algoritması: " + (header?.alg ?? "(yok)");
            return false;
        }

        // 2) İmzayı doğrula: ECDSA P-256 + SHA-256, "header.payload" üzerinde.
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

        if (signature.Length != 64)
        {
            // ES256/JWS imzası P-256 için tam olarak 64 bayt (32 bayt R + 32
            // bayt S, IEEE P1363 ham birleştirme) olmalıdır. PyJWT dahil
            // standart JWT kütüphaneleri bu formatı üretir.
            error = $"İmza uzunluğu geçersiz (beklenen 64 bayt, gelen {signature.Length}).";
            return false;
        }

        bool signatureValid;

        try
        {
            byte[] hash;

            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(signedData);
            }

            byte[] rBytes = new byte[32];
            byte[] sBytes = new byte[32];

            Buffer.BlockCopy(signature, 0, rBytes, 0, 32);
            Buffer.BlockCopy(signature, 32, sBytes, 0, 32);

            BigInteger r = new BigInteger(1, rBytes);
            BigInteger s = new BigInteger(1, sBytes);

            var signer = new ECDsaSigner();
            signer.Init(false, publicKey);

            signatureValid =
                signer.VerifySignature(hash, r, s);
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

    /// <summary>
    /// JWT/JWS'nin ES256 için kullandığı "ham" (IEEE P1363, R || S, her biri
    /// P-256 için 32 bayt = toplam 64 bayt) imza formatını, ECDsa.VerifyData'nın
    /// eski (her platformda desteklenen) overload'unun beklediği DER
    /// (ECDSA-Sig-Value ::= SEQUENCE { r INTEGER, s INTEGER }) formatına çevirir.
    /// </summary>
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
