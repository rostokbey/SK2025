using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// RSA anahtarlarını Base64 metin olarak dışa/içe aktarmak için, Unity'nin
/// Editor/Mono çalışma zamanıyla UYUMLU bir yol sağlar.
///
/// NEDEN BU DOSYA VAR: RSA.ExportPkcs8PrivateKey() / ImportPkcs8PrivateKey() /
/// ExportSubjectPublicKeyInfo() / ImportSubjectPublicKeyInfo() - .NET 5+'ta
/// eklenen görece yeni API'lerdir. Unity Editor'ün kullandığı Mono çalışma
/// zamanında bu metotlar IMPLEMENTE EDİLMEMİŞ olabilir ve çağrıldığında
/// "PlatformNotSupportedException: Operation is not supported on this
/// platform." hatası fırlatır (bu tam olarak yaşanan hataydı).
///
/// Çözüm: RSA'nın ham sayısal bileşenlerini (RSAParameters - Modulus,
/// Exponent, D, P, Q, DP, DQ, InverseQ) taşıyan RSA.ExportParameters() /
/// ImportParameters() kullanmak. Bu API, .NET'in en başından beri var olan,
/// Mono dahil HER çalışma zamanında desteklenen klasik yoldur. Bu dosya o
/// bileşenleri basit bir JSON zarfına koyup Base64'e çevirir (ASN.1/DER
/// encoding'e hiç gerek kalmaz).
///
/// ÖNEMLİ: JwtCardVerifier (çalışma zamanı/oyun içi) ve CardSigningTool
/// (Editor-only) AYNI formatı kullanmalı - bu yüzden bu dosya Editor-only
/// DEĞİLDİR, oyunun kendisiyle birlikte derlenir.
/// </summary>
public static class RsaKeyPortability
{
    [Serializable]
    private class PublicKeyBlob
    {
        public string Modulus;
        public string Exponent;
    }

    [Serializable]
    private class PrivateKeyBlob
    {
        public string Modulus;
        public string Exponent;
        public string D;
        public string P;
        public string Q;
        public string DP;
        public string DQ;
        public string InverseQ;
    }

    public static string ExportPublicKeyBase64(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        var blob = new PublicKeyBlob
        {
            Modulus = Convert.ToBase64String(p.Modulus),
            Exponent = Convert.ToBase64String(p.Exponent)
        };
        return WrapAsBase64(JsonUtility.ToJson(blob));
    }

    public static void ImportPublicKey(RSA rsa, string base64)
    {
        var blob = JsonUtility.FromJson<PublicKeyBlob>(UnwrapFromBase64(base64));
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Convert.FromBase64String(blob.Modulus),
            Exponent = Convert.FromBase64String(blob.Exponent)
        });
    }

    public static string ExportPrivateKeyBase64(RSA rsa)
    {
        var p = rsa.ExportParameters(true);
        var blob = new PrivateKeyBlob
        {
            Modulus = Convert.ToBase64String(p.Modulus),
            Exponent = Convert.ToBase64String(p.Exponent),
            D = Convert.ToBase64String(p.D),
            P = Convert.ToBase64String(p.P),
            Q = Convert.ToBase64String(p.Q),
            DP = Convert.ToBase64String(p.DP),
            DQ = Convert.ToBase64String(p.DQ),
            InverseQ = Convert.ToBase64String(p.InverseQ)
        };
        return WrapAsBase64(JsonUtility.ToJson(blob));
    }

    public static void ImportPrivateKey(RSA rsa, string base64)
    {
        var blob = JsonUtility.FromJson<PrivateKeyBlob>(UnwrapFromBase64(base64));
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Convert.FromBase64String(blob.Modulus),
            Exponent = Convert.FromBase64String(blob.Exponent),
            D = Convert.FromBase64String(blob.D),
            P = Convert.FromBase64String(blob.P),
            Q = Convert.FromBase64String(blob.Q),
            DP = Convert.FromBase64String(blob.DP),
            DQ = Convert.FromBase64String(blob.DQ),
            InverseQ = Convert.FromBase64String(blob.InverseQ)
        });
    }

    private static string WrapAsBase64(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static string UnwrapFromBase64(string base64) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
