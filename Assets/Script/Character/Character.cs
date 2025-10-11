using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class Character : NetworkBehaviour
{
    [Header("Stats")]
    public CardData cardData;
    public float maxHp;
    public float defense = 0f;

    private HealthSystem health;

    [Header("State")]
    public bool isMyTurn;
    public bool isPlayer;   // ✅ eklendi

    [Header("Optional")]
    public Animator animator;

    // Setup çağrısı
    public void Setup(CardData data)
    {
        cardData = data;

        if (health == null) health = GetComponent<HealthSystem>();
        if (health != null)
        {
            health.maxHP = data.baseHP;
            if (IsServer)
            {
                health.currentHP.Value = data.baseHP;
                health.shield = 0;
            }
        }

        maxHp = data.baseHP;
        defense = 0f;
        name = data.cardName;
    }

    public void SetTurn(bool turn)
    {
        isMyTurn = turn;
    }

    [ServerRpc(RequireOwnership = false)]
    public void AttackServerRpc(NetworkObjectReference targetRef)
    {
        if (!isMyTurn) return;

        if (targetRef.TryGet(out var targetObj))
        {
            var targetHS = targetObj.GetComponent<HealthSystem>();
            if (targetHS != null)
            {
                int dmg = Mathf.Max(1, cardData.baseDamage);
                targetHS.TakeDamageServerRpc(dmg);
                Debug.Log($"[CHAR] {cardData.cardName} vurdu: {dmg}");
            }
        }

        BattleManager.Instance?.EndTurn();
    }
}
