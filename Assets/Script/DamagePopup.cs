using UnityEngine;
using TMPro;

public class DamagePopup : MonoBehaviour
{
    [Header("Refs")]
    public TMP_Text text;                 // Prefab içindeki TMP_Text (atanmazsa Awake'te bulunur)

    [Header("Anim")]
    public float life = 0.8f;             // Ekranda kalma süresi
    public Vector3 drift = new Vector3(0f, 1.5f, 0f); // Yukarý kayma yönü
    public float speed = 2f;              // Kayma hýzý

    void Awake()
    {
        if (!text) text = GetComponentInChildren<TMP_Text>();
    }

    // --- Basit kullaným: sadece sayý göster ---
    public void Setup(int amount)
    {
        if (!text) text = GetComponentInChildren<TMP_Text>();

        text.text = amount.ToString();
        text.color = Color.white;         // Düz hasar rengi
        transform.localScale = Vector3.one * 0.01f; // 3D sahnede okunaklý ölçek
    }

    // --- Ýsteðe baðlý: eski çaðrýlar boþa düþmesin diye çok parametreli overloada izin veriyoruz ---
    public void Setup(int amount, bool isCritical, bool isHeal, bool isShieldGain)
    {
        Setup(amount);                    // Temel yazýyý ayarla
        if (isHeal) text.color = Color.green;
        else if (isCritical) text.color = Color.yellow;
        else text.color = Color.white;
        // isShieldGain için özel efekt eklemek istersen burada rengi/metin biçimini deðiþtirebilirsin.
    }

    void Update()
    {
        // Hafif yukarý doðru süzülme
        transform.position += drift * speed * Time.deltaTime;

        // Ömür
        life -= Time.deltaTime;
        if (life <= 0f) Destroy(gameObject);
    }

    // --- Opsiyonel factory: Script'ten rahat çaðýrmak için ---
    // Resources/Prefabs/FX/DamagePopup yolunda prefab varsa bunu kullanýr.
    public static DamagePopup Create(Vector3 worldPos, int amount)
    {
        var prefab = Resources.Load<GameObject>("Prefabs/FX/DamagePopup");
        if (!prefab)
        {
            Debug.LogWarning("DamagePopup prefab bulunamadý: Resources/Prefabs/FX/DamagePopup");
            return null;
        }

        var go = Instantiate(prefab, worldPos, Quaternion.identity);
        var dp = go.GetComponent<DamagePopup>();
        dp.Setup(amount);
        return dp;
    }
}
