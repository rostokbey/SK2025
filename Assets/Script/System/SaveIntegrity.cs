using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Yerel kayıt dosyalarının (envanter, deste vb.) bütünlüğünü korumak için
/// HMAC-SHA256 tabanlı imzalama/doğrulama yardımcı sınıfı.
///
/// ÖNEMLİ - GERÇEKÇİ TEHDİT MODELİ:
/// Bu proje bir arka uç sunucu içermiyor (tüm oyun verisi cihazda tutuluyor),
/// bu yüzden "gerçek" bir güvenlik için tek doğru çözüm verileri bir sunucuda
/// tutup istemciye hiç güvenmemektir. Bu, mevcut mimariyi baştan yazmayı
/// gerektirir. Sunucu olmadığı sürece, sıradan bir oyuncunun JSON dosyasını
/// elle açıp kart/XP değerlerini değiştirmesini (save-editing) engelleyen,
/// makul ve düşük riskli bir "kurcalama tespiti" katmanı ekliyoruz:
///
///   - Her kayıt dosyası, cihaza özgü bir anahtarla HMAC-SHA256 imzalanır.
///   - Yükleme sırasında imza yeniden hesaplanır; eşleşmezse dosya
///     "bozulmuş/kurcalanmış" kabul edilir ve OYUNA YÜKLENMEZ.
///   - Anahtar cihaz kimliğine (SystemInfo.deviceUniqueIdentifier) bağlı
///     olduğu için bir oyuncunun kayıt dosyasını başka bir cihaza kopyalayıp
///     kullanması da anlamsız hale gelir (imza tutmaz).
///
/// Bu yöntem SIFIR-GÜVEN (yani sunucu tarafı doğrulama) sağlamaz; motive bir
/// saldırgan uygulamayı tersine mühendislikle inceleyip anahtar türetme
/// mantığını bulabilir. Amaç, "kolay/gündelik" hileyi (JSON dosyasını metin
/// editörüyle açıp değiştirmeyi) engellemek ve gelecekte gerçek bir sunucu
/// eklendiğinde bu katmanın "ilk savunma hattı" olarak kalmasıdır.
/// </summary>
public static class SaveIntegrity
{
    // Uygulamaya gömülü "pepper" (ek gizli değer). Bu, cihaz kimliğiyle
    // birlikte anahtar türetmede kullanılır. Tek başına bir sır olarak
    // güvenilmemeli; asıl koruma cihaz-bağımlılığından gelir.
    // İsteğe bağlı sertleştirme: Bu sabiti derleme zamanında native bir
    // eklentiye taşımak veya birkaç parçaya bölüp çalışma zamanında
    // birleştirmek, statik analizle bulunmasını zorlaştırır.
    private const string AppPepper = "SK2025-Savas-Kartlari-Local-Save-Pepper-v1";

    /// <summary>
    /// Cihaz + profil + amaç (ör. "inventory", "decks") bilgisine bağlı,
    /// deterministik bir HMAC anahtarı türetir.
    /// </summary>
    private static byte[] DeriveKey(string profileId, string purpose)
    {
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        string material = $"{AppPepper}|{deviceId}|{profileId}|{purpose}";

        using (var sha256 = SHA256.Create())
        {
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
        }
    }

    /// <summary>Verilen JSON içeriği için imza (hex) üretir.</summary>
    public static string Sign(string json, string profileId, string purpose)
    {
        byte[] key = DeriveKey(profileId, purpose);

        using (var hmac = new HMACSHA256(key))
        {
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
            return ToHex(hash);
        }
    }

    // Convert.ToHexString .NET 5+ API'si; Unity'nin .NET Standard 2.1 çalışma
    // zamanında bulunmayabileceği için taşınabilir/manuel bir hex encoder.
    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>İmzanın verilen JSON içeriğiyle eşleşip eşleşmediğini sabit-zamanlı karşılaştırma ile kontrol eder.</summary>
    public static bool Verify(string json, string signatureHex, string profileId, string purpose)
    {
        if (string.IsNullOrEmpty(signatureHex))
            return false;

        string expected = Sign(json, profileId, purpose);
        return FixedTimeEquals(expected, signatureHex);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];

        return diff == 0;
    }
}
