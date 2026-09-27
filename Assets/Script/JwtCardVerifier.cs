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
        "REPLACE_WITH_REAL_PUBLIC_KEY_FROM_export_ec_public_key_py";

    private static ECDsa _cachedPublicKey;

    private static ECDsa GetPublicKey()
    {
        if (_cachedPublicKey != null)
            return _cachedPublicKey;

        if (string.IsNullOrEmpty(CardSigningPublicKeyBase64) ||
            CardSigningPublicKeyBase64.StartsWith("REPLACE_WITH_"))
        {
            Debug.LogError("[JwtCardVerifier] Açık anahtar ayarlanmamış! " +
                            "export_ec_public_key.py ec_private.pem ile açık " +
                            "anahtarı üretip CardSigningPublicKeyBase64 alanını " +
                            "güncelleyin. Anahtar ayarlanana kadar TÜM imzalı " +
                            "kartlar reddedilecek.");
            return null;
        }

        try
        {
            var ecdsa = ECDsa.Create();
            EcKeyPortability.ImportPublicKey(ecdsa, CardSigningPublicKeyBase64);
            _cachedPublicKey = ecdsa;
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
            // Tercih edilen yol: .NET 5+'ta eklenen, JWT'nin ham (P1363)
            // imza formatını DOĞRUDAN doğrulayabilen overload.
            signatureValid = publicKey.VerifyData(
                signedData, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (PlatformNotSupportedException)
        {
            // Bazı Unity/Mono çalışma zamanlarında yukarıdaki .NET5+ overload'u
            // yok (RSA.ExportPkcs8PrivateKey'de yaşadığımızla aynı sorun
            // sınıfı). Bu durumda ham imzayı manuel olarak DER'e çevirip her
            // yerde desteklenen eski overload ile doğrula.
            try
            {
                byte[] derSignature = ConvertP1363SignatureToDer(signature);
                signatureValid = publicKey.VerifyData(signedData, derSignature, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {
                error = "İmza doğrulama hatası (DER fallback): " + ex.Message;
                return false;
            }
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
    private static byte[] ConvertP1363SignatureToDer(byte[] p1363Signature)
    {
        int len = p1363Signature.Length / 2;
        byte[] r = new byte[len];
        byte[] s = new byte[len];
        Array.Copy(p1363Signature, 0, r, 0, len);
        Array.Copy(p1363Signature, len, s, 0, len);

        byte[] rEnc = EncodeDerInteger(r);
        byte[] sEnc = EncodeDerInteger(s);

        byte[] seqContent = new byte[rEnc.Length + sEnc.Length];
        Buffer.BlockCopy(rEnc, 0, seqContent, 0, rEnc.Length);
        Buffer.BlockCopy(sEnc, 0, seqContent, rEnc.Length, sEnc.Length);

        byte[] seqLenEnc = EncodeDerLength(seqContent.Length);
        byte[] result = new byte[1 + seqLenEnc.Length + seqContent.Length];
        result[0] = 0x30; // DER SEQUENCE tag
        Buffer.BlockCopy(seqLenEnc, 0, result, 1, seqLenEnc.Length);
        Buffer.BlockCopy(seqContent, 0, result, 1 + seqLenEnc.Length, seqContent.Length);
        return result;
    }

    private static byte[] EncodeDerInteger(byte[] unsignedBigEndian)
    {
        int start = 0;
        while (start < unsignedBigEndian.Length - 1 && unsignedBigEndian[start] == 0x00)
            start++;

        int contentLen = unsignedBigEndian.Length - start;
        bool needsLeadingZero = (unsignedBigEndian[start] & 0x80) != 0;

        int totalLen = contentLen + (needsLeadingZero ? 1 : 0);
        byte[] lenEnc = EncodeDerLength(totalLen);

        byte[] result = new byte[1 + lenEnc.Length + totalLen];
        result[0] = 0x02; // DER INTEGER tag
        Buffer.BlockCopy(lenEnc, 0, result, 1, lenEnc.Length);

        int offset = 1 + lenEnc.Length;
        if (needsLeadingZero)
        {
            result[offset] = 0x00;
            offset++;
        }
        Buffer.BlockCopy(unsignedBigEndian, start, result, offset, contentLen);
        return result;
    }

    private static byte[] EncodeDerLength(int length)
    {
        if (length < 0x80)
            return new byte[] { (byte)length };

        // Uzun form (P-256 imzalarının R/S bileşenleri için pratikte hiç
        // tetiklenmez - en fazla 33 bayt olurlar - ama tam olsun diye var).
        int byteCount = 0;
        int tmp = length;
        while (tmp > 0) { byteCount++; tmp >>= 8; }

        byte[] result = new byte[1 + byteCount];
        result[0] = (byte)(0x80 | byteCount);
        for (int i = 0; i < byteCount; i++)
            result[1 + i] = (byte)(length >> (8 * (byteCount - 1 - i)));

        return result;
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
