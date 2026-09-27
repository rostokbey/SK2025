using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// EC (P-256 / secp256r1) anahtarlarını, Unity'nin bazı platformlarda/
/// çalışma zamanlarında desteklemediği modern PEM/SubjectPublicKeyInfo
/// Export/Import metotları (ExportSubjectPublicKeyInfo, ImportECPrivateKey
/// vb. - .NET 5+ API'leri, RSA tarafında yaşadığımız
/// PlatformNotSupportedException ile AYNI risk sınıfı) YERİNE,
/// ECDsa.ExportParameters/ImportParameters (ECParameters struct - çok daha
/// eski ve evrensel desteklenen API) üzerinden kendi basit/taşınabilir
/// metin formatına kodlar.
///
/// Bu format standart bir PEM değildir - sadece bu projenin kendi
/// doğrulayıcısı (JwtCardVerifier) ile anlaşmalı bir serileştirmedir.
/// Python tarafında (mint_qr_tokens.py ile aynı pipeline) bu formatı
/// üretmek için export_ec_public_key.py script'i verilmiştir.
/// </summary>
public static class EcKeyPortability
{
    [Serializable] private class PublicBlob { public string x; public string y; }
    [Serializable] private class PrivateBlob { public string x; public string y; public string d; }

    public static string ExportPublicKeyBase64(ECDsa ecdsa)
    {
        var p = ecdsa.ExportParameters(false);
        var blob = new PublicBlob
        {
            x = Convert.ToBase64String(p.Q.X),
            y = Convert.ToBase64String(p.Q.Y)
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(blob)));
    }

    public static string ExportPrivateKeyBase64(ECDsa ecdsa)
    {
        var p = ecdsa.ExportParameters(true);
        var blob = new PrivateBlob
        {
            x = Convert.ToBase64String(p.Q.X),
            y = Convert.ToBase64String(p.Q.Y),
            d = Convert.ToBase64String(p.D)
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(blob)));
    }

    public static void ImportPublicKey(ECDsa ecdsa, string base64Blob)
    {
        var blob = ParseBlob<PublicBlob>(base64Blob);
        ecdsa.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromBase64String(blob.x),
                Y = Convert.FromBase64String(blob.y)
            }
        });
    }

    public static void ImportPrivateKey(ECDsa ecdsa, string base64Blob)
    {
        var blob = ParseBlob<PrivateBlob>(base64Blob);
        ecdsa.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromBase64String(blob.x),
                Y = Convert.FromBase64String(blob.y)
            },
            D = Convert.FromBase64String(blob.d)
        });
    }

    private static T ParseBlob<T>(string base64Blob)
    {
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Blob));
        return JsonUtility.FromJson<T>(json);
    }
}
