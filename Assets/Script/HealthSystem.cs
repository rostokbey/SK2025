using UnityEngine;
using Unity.Netcode;
using SK2025.VFX;

public class HealthSystem : NetworkBehaviour
{
    [Header("HP & Shield")]
    public int maxHP = 100;
    public NetworkVariable<int> currentHP = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int shield = 0;

    [Header("FX")]
    public GameObject damagePopupPrefab;
    public Animator animator; // ✅ Animasyon eklendi
    public GameObject deathEffect; // opsiyonel efekt

    // Karaktere özel VFX araması için temiz isim (örn: "Ates_Ciragi3D")
    private string vfxKey;
    private Transform vfxAnchor;

    void Awake()
    {
        if (maxHP <= 0) maxHP = 100;

        vfxKey = gameObject.name.Replace("(Clone)", "").Trim();
        vfxAnchor = FindVFXAnchor();
    }
    private Transform FindVFXAnchor()
    {
        // Karakter prefabında VFXAnchor varsa onu kullan.
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "VFXAnchor")
                return child;
        }

        // Anchor yoksa eski davranış: karakterin root noktası.
        return transform;
    }

    private Vector3 GetVFXPosition()
    {
        return vfxAnchor != null ? vfxAnchor.position : transform.position;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && currentHP.Value <= 0)
        {
            currentHP.Value = maxHP;
        }
    }

    // ====== Server API ======
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(int damage)
    {
        // Geriye dönük uyumluluk. Savaş hesapları sunucuda ApplyDamageServerSide kullanır.
        ApplyDamageServerSide(damage, false);
    }

    public void ApplyDamageServerSide(int damage, bool isCritical)
    {
        if (!IsServer || damage <= 0) return;

        int beforeShield = shield;

        int shieldAbsorb = Mathf.Min(shield, damage);
        shield -= shieldAbsorb;

        int remaining = Mathf.Max(0, damage - shieldAbsorb);
        currentHP.Value = Mathf.Max(0, currentHP.Value - remaining);

        Debug.Log($"[HS] Hasar {damage} (kalkan {beforeShield}->{shield}) kalan HP: {currentHP.Value}");

        // ✅ Hit animasyonu
        if (animator != null)
            animator.SetTrigger("Hit");

        ShowDamageClientRpc(remaining, isCritical, false, false);

        if (currentHP.Value <= 0)
            Die();
    }

    [ServerRpc(RequireOwnership = false)]
    public void HealServerRpc(int amount)
    {
        HealServerSide(amount);
    }

    /// <summary>
    /// Sunucu tarafında doğrudan çağrılacak iyileştirme (RPC değil - zaten
    /// sunucuda çalışan kodlardan, ör. pasif yetenekler / round-start
    /// hook'larından çağrılmak için). Bir istemciden gelen isteği hiçbir
    /// zaman doğrudan işlemez.
    /// </summary>
    public void HealServerSide(int amount)
    {
        if (!IsServer || amount <= 0) return;
        int old = currentHP.Value;
        currentHP.Value = Mathf.Min(maxHP, currentHP.Value + amount);
        int gained = currentHP.Value - old;

        if (gained > 0)
            ShowDamageClientRpc(gained, false, true, false);
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddShieldServerRpc(int amount)
    {
        if (amount <= 0) return;
        shield += amount;
        ShowDamageClientRpc(amount, false, false, true);
    }

    // ====== Client FX ======
    [ClientRpc]
    private void ShowDamageClientRpc(int amount, bool isCritical, bool isHeal, bool isShieldGain)
    {
        // ─── VFX ───
        // Tüm hasar/iyileşme kaynakları (saldırı, beceri, pasif) buradan geçtiği
        // için karaktere özel Hit/Heal efektlerinin tek doğru bağlantı noktası burası.
        if (VFXManager.Instance != null)
        {
            if (isHeal)
            {
                VFXManager.Instance.PlayCharacterVFX(vfxKey, "Heal", GetVFXPosition());
            }
            else if (isShieldGain)
            {
                VFXManager.Instance.PlayCharacterVFX(vfxKey, "Shield", GetVFXPosition());
            }
            else if (amount > 0)
            {
                VFXManager.Instance.PlayCharacterVFX(vfxKey, "Hit", GetVFXPosition());
            }
        }

        // ─── Hasar yazısı (popup) ───
        if (damagePopupPrefab == null) return;

        var pos = transform.position + Vector3.up * 1.5f;
        var popup = Object.Instantiate(damagePopupPrefab, pos, Quaternion.identity);

        var dp = popup.GetComponent<DamagePopup>();
        if (dp != null)
        {
            dp.Setup(amount, isCritical, isHeal, isShieldGain);
        }
    }

    // ====== Utils ======
    private void Die()
    {
        Debug.Log($"[HS] {gameObject.name} öldü.");

        // ✅ Die animasyonu
        if (animator != null)
            animator.SetTrigger("Die");

        // Ölüm efekti tüm istemcilerde oynasın.
        PlayDeathFXClientRpc();

        // NOT (round/ability sistemi güncellemesi): Önceden burada NetworkObject
        // despawn ediliyordu (ölüm = kalıcı yok oluş). Tasarım dokümanındaki
        // "karakter ölürse [yetenek] bekleme süresi sıfırlanmaz, yeniden
        // dirilirse kalan süreyle devam eder" kuralı, ölen karakterin
        // durumunun (özellikle Character.AbilityCooldownRemaining) sahnede
        // KORUNMASINI gerektiriyor. Bu yüzden artık despawn ETMİYORUZ; sadece
        // "düşmüş" (downed) olarak işaretliyoruz. IsAlive (currentHP > 0)
        // zaten hedefleme/tur sıralaması gibi tüm sistemlerde tek doğruluk
        // kaynağı olarak kullanılıyor (bkz. BattleGrid, Character.IsAlive).
        var character = GetComponent<Character>();
        if (character != null)
            character.SetDownedVisual(true);

        // ✅ Victory kontrolü
        BattleManager.Instance?.CheckVictory();
    }

    [ClientRpc]
    private void PlayDeathFXClientRpc()
    {
        // Karaktere özel ölüm efekti: "Ates_Ciragi3D_Death" arar.
        if (VFXManager.Instance != null)
            VFXManager.Instance.PlayCharacterVFX(vfxKey, "Death", GetVFXPosition());

        // Inspector'dan atanmış eski tip deathEffect varsa onu da oynat.
        if (deathEffect != null)
            Instantiate(deathEffect, transform.position, Quaternion.identity);
    }
}