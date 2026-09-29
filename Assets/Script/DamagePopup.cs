using UnityEngine;
using TMPro;

public class DamagePopup : MonoBehaviour
{
    [Header("Refs")]
    public TMP_Text text;                 // Prefab içindeki TMP_Text (atanmazsa Awake'te bulunur)

    [Header("Anim")]
    public float life = 0.8f;             // Ekranda kalma süresi
    public Vector3 drift = new Vector3(0f, 1.5f, 0f); // Yukarı kayma yönü
    public float speed = 2f;              // Kayma hızı

    void Awake()
    {
        if (!text) text = GetComponentInChildren<TMP_Text>();
    }

    // --- Basit kullanım: sadece sayı göster ---
    public void Setup(int amount)
    {
        if (!text) text = GetComponentInChildren<TMP_Text>();

        text.text = amount.ToString();
        text.color = Color.white;         // Düz hasar rengi
        transform.localScale = Vector3.one * 0.01f; // 3D sahnede okunaklı ölçek
    }

    // --- İsteğe bağlı: eski çağrılar boşa düşmesin diye çok parametreli overloada izin veriyoruz ---
    public void Setup(int amount, bool isCritical, bool isHeal, bool isShieldGain)
    {
        Setup(amount);                    // Temel yazıyı ayarla
        if (isShieldGain) text.color = new Color(0.20f, 0.75f, 1f);
        else if (isHeal) text.color = Color.green;
        else if (isCritical) text.color = new Color(1f, 0.55f, 0.05f);
        else text.color = Color.white;
        // isShieldGain için özel efekt eklemek istersen burada rengi/metin biçimini değiştirebilirsin.
    }

    void Update()
    {
        // Hafif yukarı doğru süzülme
        transform.position += drift * speed * Time.deltaTime;

        // Ömür
        life -= Time.deltaTime;
        if (life <= 0f) Destroy(gameObject);
    }

    // --- Opsiyonel factory: Script'ten rahat çağırmak için ---
    // Resources/Prefabs/FX/DamagePopup yolunda prefab varsa bunu kullanır.
    public static DamagePopup Create(Vector3 worldPos, int amount)
    {
        var prefab = Resources.Load<GameObject>("Prefabs/FX/DamagePopup");
        if (!prefab)
        {
            Debug.LogWarning("DamagePopup prefab bulunamadı: Resources/Prefabs/FX/DamagePopup");
            return null;
        }

        var go = Instantiate(prefab, worldPos, Quaternion.identity);
        var dp = go.GetComponent<DamagePopup>();
        dp.Setup(amount);
        return dp;
    }
}
