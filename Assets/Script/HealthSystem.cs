using UnityEngine;
using Unity.Netcode;

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

    void Awake()
    {
        if (maxHP <= 0) maxHP = 100;
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
        if (damagePopupPrefab == null) return;

        var pos = transform.position + Vector3.up * 1.5f;
        var popup = Object.Instantiate(damagePopupPrefab, pos, Quaternion.identity);

        var dp = popup.GetComponent<DamagePopup>();
        if (dp != null)
        {
            dp.Setup(amount);
        }
    }

    // ====== Utils ======
    private void Die()
    {
        Debug.Log($"[HS] {gameObject.name} öldü.");

        // ✅ Die animasyonu
        if (animator != null)
            animator.SetTrigger("Die");

        if (deathEffect != null)
            Instantiate(deathEffect, transform.position, Quaternion.identity);

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
}
