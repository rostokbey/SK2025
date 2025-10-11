using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleInviteMock : MonoBehaviour
{
    public static BattleInviteMock Instance { get; private set; }
    public event Action OnChanged;

    private readonly List<BattleInvite> _pending = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Touch() => OnChanged?.Invoke();

    public IReadOnlyList<BattleInvite> GetInbox(string playerId)
        => _pending.Where(p => p.ToPlayerId == playerId).ToList();

    public void Send(string fromId, string toId)
    {
        _pending.Add(new BattleInvite
        {
            InviteId = Guid.NewGuid().ToString(),
            FromPlayerId = fromId,
            ToPlayerId = toId
        });
        Touch();
    }

    public void Accept(string inviteId, string acceptorId, Action onAccepted)
    {
        var inv = _pending.FirstOrDefault(i => i.InviteId == inviteId && i.ToPlayerId == acceptorId);
        if (inv == null) return;
        _pending.Remove(inv);
        Touch();
        onAccepted?.Invoke(); // Burada StartBattleManager’ý tetikleyebilirsin
    }

    public void Decline(string inviteId, string declinerId)
    {
        var inv = _pending.FirstOrDefault(i => i.InviteId == inviteId && i.ToPlayerId == declinerId);
        if (inv == null) return;
        _pending.Remove(inv);
        Touch();
    }
}
