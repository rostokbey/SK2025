#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kart üretim/imzalama için EDITOR-ONLY yardımcı pencere.
///
/// Bu araç, JwtCardVerifier'ın doğrulayabileceği RS256 imzalı kart QR
/// metinleri üretmenizi sağlar:
///   1) "Generate New RSA KeyPair" ile bir anahtar çifti oluşturun.
///   2) Açık anahtarı (public key) JwtCardVerifier.CardSigningPublicKeyBase64
///      sabitine yapıştırın (istemciye gömülecek olan budur).
///   3) Özel anahtarı (private key) GÜVENLİ bir yerde saklayın - bu repoya
///      veya derlenmiş uygulamaya ASLA eklenmemeli. Gerçek üretimde bu
///      aracın kendisinin de oyun projesinden ayrı, sadece kart basım
///      ekibinin erişebildiği bir ortamda çalışması önerilir.
///   4) Kart alanlarını doldurup "Sign Card" ile TEK bir QR metni (test/
///      önizleme) veya "Batch Sign" ile AYNI karakterin binlerce fiziksel
///      kopyası için birden fazla QR metni üretin (bkz. aşağıdaki not).
///
/// ÇOK SAYIDA FİZİKSEL KART (ör. aynı karakterden 10.000 adet) NASIL ÇALIŞIR:
/// cardId/isim/HP/STR/DEF/yetenek gibi alanlar bir KARAKTER TÜRÜNÜ tanımlar
/// ve o türün TÜM fiziksel kopyalarında AYNI kalır - bunları 10.000 kez elle
/// girmenize gerek yoktur. Her fiziksel kartı birbirinden ayıran tek şey
/// "jti" (benzersiz seri numarası) alanıdır, ki bu zaten her imzalamada
/// otomatik ve rastgele üretilir. Bu yüzden akış şudur: karakterin
/// bilgilerini BİR KEZ girin, "Kaç adet?" kutusuna 10000 yazın, "Batch Sign"
/// ile hepsini tek seferde bir metin dosyasına (her satırda bir QR içeriği)
/// üretin ve bu dosyayı QR/kart basım sürecinize verin.
/// </summary>
public class CardSigningTool : EditorWindow
{
    private string _privateKeyBase64 = "";
    private string _publicKeyBase64 = "";

    private string _cardId = "CARD-0001";
    private string _cardName = "Örnek Kart";
    private string _rarity = "epic";
    private int _hp = 100;
    private int _str = 20;
    private int _def = 10;
    private string _ability = "";
    private string _passive = "";
    private string _prefab = "";
    private int _expiryDays = 0; // 0 = süresiz

    private int _batchCount = 1;

    private string _outputJwt = "";

    [MenuItem("Tools/Kartlar/Card Signing Tool")]
    public static void ShowWindow()
    {
        GetWindow<CardSigningTool>("Card Signing Tool");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Bu pencere yalnızca kart üretim/test amaçlıdır. Üretilen ÖZEL " +
            "anahtarı repoya veya oyuna asla eklemeyin.",
            MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1) Anahtar Çifti", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate New RSA KeyPair (2048-bit)"))
        {
            GenerateKeyPair();
        }

        EditorGUILayout.LabelField("Private Key (GİZLİ TUT)");
        _privateKeyBase64 = EditorGUILayout.TextArea(_privateKeyBase64, GUILayout.Height(60));

        EditorGUILayout.LabelField("Public Key (JwtCardVerifier'a yapıştır)");
        _publicKeyBase64 = EditorGUILayout.TextArea(_publicKeyBase64, GUILayout.Height(60));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2) Karakter Bilgileri (bu türün TÜM fiziksel kopyalarında aynı kalır)", EditorStyles.boldLabel);

        _cardId = EditorGUILayout.TextField("Card Id", _cardId);
        _cardName = EditorGUILayout.TextField("Name", _cardName);
        _rarity = EditorGUILayout.TextField("Rarity", _rarity);
        _hp = EditorGUILayout.IntField("HP", _hp);
        _str = EditorGUILayout.IntField("STR", _str);
        _def = EditorGUILayout.IntField("DEF", _def);
        _ability = EditorGUILayout.TextField("Ability", _ability);
        _passive = EditorGUILayout.TextField("Passive", _passive);
        _prefab = EditorGUILayout.TextField("Prefab (Resources path)", _prefab);
        _expiryDays = EditorGUILayout.IntField("Expiry (gün, 0 = süresiz)", _expiryDays);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3) Tek Kart (test/önizleme)", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_privateKeyBase64)))
        {
            if (GUILayout.Button("Sign Card (1 adet, ekranda göster)"))
            {
                SignCard();
            }
        }

        if (!string.IsNullOrEmpty(_outputJwt))
        {
            EditorGUILayout.TextArea(_outputJwt, GUILayout.Height(80));

            if (GUILayout.Button("Copy to Clipboard"))
            {
                EditorGUIUtility.systemCopyBuffer = _outputJwt;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("4) Toplu Üretim (aynı karakterin N fiziksel kopyası)", EditorStyles.boldLabel);

        _batchCount = EditorGUILayout.IntField("Kaç adet?", _batchCount);

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_privateKeyBase64) || _batchCount < 1))
        {
            if (GUILayout.Button($"Batch Sign ({_batchCount} adet) ve Dosyaya Kaydet..."))
            {
                BatchSignToFile(_batchCount);
            }
        }
    }

    private void GenerateKeyPair()
    {
        using (var rsa = RSA.Create(2048))
        {
            _privateKeyBase64 = RsaKeyPortability.ExportPrivateKeyBase64(rsa);
            _publicKeyBase64 = RsaKeyPortability.ExportPublicKeyBase64(rsa);
        }

        Debug.Log("[CardSigningTool] Yeni anahtar çifti üretildi. Public key'i JwtCardVerifier'a, " +
                   "private key'i güvenli bir kasaya kaydedin.");
    }

    private CardPayload BuildPayload()
    {
        return new CardPayload
        {
            typ = "CARD",
            ver = 1,
            id = _cardId,
            name = _cardName,
            rarity = _rarity,
            hp = _hp,
            str = _str,
            def = _def,
            ability = _ability,
            passive = _passive,
            prefab = _prefab,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = _expiryDays > 0
                ? DateTimeOffset.UtcNow.AddDays(_expiryDays).ToUnixTimeSeconds()
                : 0,
            jti = Guid.NewGuid().ToString("N") // her fiziksel kartın kendine özgü seri numarası
        };
    }

    private void SignCard()
    {
        try
        {
            _outputJwt = Sign(BuildPayload(), _privateKeyBase64);
            Debug.Log("[CardSigningTool] Kart imzalandı: " + _cardId);
        }
        catch (Exception ex)
        {
            Debug.LogError("[CardSigningTool] İmzalama hatası: " + ex.Message);
        }
    }

    /// <summary>
    /// AYNI karakterden N fiziksel kopya için N ayrı, benzersiz jti'li QR
    /// metni üretir ve seçilen dosyaya bir satıra bir QR içeriği gelecek
    /// şekilde yazar. 10.000 gibi büyük sayılarda birkaç saniye/dakika
    /// sürebilir - ilerleme çubuğu gösterilir.
    /// </summary>
    private void BatchSignToFile(int count)
    {
        string defaultName = SanitizeFileName(_cardId) + $"_x{count}.txt";
        string path = EditorUtility.SaveFilePanel("QR metinlerini kaydet", "", defaultName, "txt");

        if (string.IsNullOrEmpty(path))
            return; // kullanıcı iptal etti

        try
        {
            using (var rsa = RSA.Create())
            {
                RsaKeyPortability.ImportPrivateKey(rsa, _privateKeyBase64);

                using (var writer = new StreamWriter(path, false, Encoding.UTF8))
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (i % 25 == 0 || i == count - 1)
                        {
                            bool cancel = EditorUtility.DisplayCancelableProgressBar(
                                "Kartlar imzalanıyor",
                                $"{i + 1} / {count}",
                                (float)(i + 1) / count);

                            if (cancel)
                            {
                                Debug.LogWarning($"[CardSigningTool] Batch imzalama iptal edildi. {i}/{count} yazıldı: {path}");
                                return;
                            }
                        }

                        string jwt = SignWithKey(BuildPayload(), rsa);
                        writer.WriteLine(jwt);
                    }
                }
            }

            Debug.Log($"[CardSigningTool] {count} adet QR metni yazıldı: {path}\n" +
                      "Her satır ayrı bir fiziksel karta basılacak QR içeriğidir " +
                      "(hepsi aynı karakter, farklı seri numarası).");

            EditorUtility.RevealInFinder(path);
        }
        catch (Exception ex)
        {
            Debug.LogError("[CardSigningTool] Batch imzalama hatası: " + ex.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "kart";
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }

    /// <summary>Bir CardPayload'ı RS256 ile imzalayıp "header.payload.signature" JWT metnini döndürür (tek seferlik kullanım - anahtarı her çağrıda yeniden içe aktarır).</summary>
    public static string Sign(CardPayload payload, string privateKeyBase64)
    {
        using (var rsa = RSA.Create())
        {
            RsaKeyPortability.ImportPrivateKey(rsa, privateKeyBase64);
            return SignWithKey(payload, rsa);
        }
    }

    /// <summary>Aynı ile RSA örneğini tekrar tekrar kullanarak imzalar - toplu (batch) üretimde performans için tercih edilir.</summary>
    private static string SignWithKey(CardPayload payload, RSA rsa)
    {
        string headerJson = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        string payloadJson = JsonUtility.ToJson(payload);

        string headerPart = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        string payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        string signingInput = headerPart + "." + payloadPart;

        byte[] signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return signingInput + "." + Base64UrlEncode(signature);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
#endif
