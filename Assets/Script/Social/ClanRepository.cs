using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ClanRepository : MonoBehaviour
{
    public static ClanRepository Instance { get; private set; }
    public event Action OnDataChanged;

    // Mock depolar
    private readonly List<Clan> _clans = new();
    private readonly List<ClanInvite> _invites = new();
    // Hýzlý sorgu için: playerId -> clanId
    private readonly Dictionary<string, string> _playerClan = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SeedIfEmpty();
    }

    void SeedIfEmpty()
    {
        if (_clans.Count > 0) return;

        // Örnek: 2 klan + birkaç üye
        var c1 = new Clan { Id = Guid.NewGuid().ToString(), Name = "KýzýlKalkan", OwnerId = "P1" };
        c1.MemberIds.AddRange(new[] { "P1", "P2" });
        var c2 = new Clan { Id = Guid.NewGuid().ToString(), Name = "GeceYürüyenler", OwnerId = "P3" };
        c2.MemberIds.AddRange(new[] { "P3" });

        _clans.Add(c1); _clans.Add(c2);
        _playerClan["P1"] = c1.Id;
        _playerClan["P2"] = c1.Id;
        _playerClan["P3"] = c2.Id;
    }

    void Touch() => OnDataChanged?.Invoke();

    // === Okuma ===
    public IReadOnlyList<Clan> GetAllClans() => _clans;
    public string GetMyClanId(string playerId) => _playerClan.TryGetValue(playerId, out var id) ? id : null;
    public Clan GetClan(string clanId) => _clans.FirstOrDefault(c => c.Id == clanId);
    public IReadOnlyList<ClanInvite> GetInvitesFor(string playerId) => _invites.Where(i => i.ToPlayerId == playerId).ToList();

    // === Yazma ===
    public bool TryCreateClan(string name, string ownerId, out Clan created)
    {
        created = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (_playerClan.ContainsKey(ownerId)) return false; // zaten bir klanda

        var clan = new Clan
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            OwnerId = ownerId,
            MemberIds = new List<string> { ownerId }
        };
        _clans.Add(clan);
        _playerClan[ownerId] = clan.Id;
        created = clan;
        Touch();
        return true;
    }

    public bool SendInvite(string clanId, string fromId, string toId)
    {
        if (string.IsNullOrEmpty(clanId)) return false;
        if (_playerClan.ContainsKey(toId)) return false; // hedef zaten bir klanda

        var inv = new ClanInvite
        {
            InviteId = Guid.NewGuid().ToString(),
            ClanId = clanId,
            FromPlayerId = fromId,
            ToPlayerId = toId
        };
        _invites.Add(inv);
        Touch();
        return true;
    }

    public bool AcceptInvite(string inviteId, string acceptorId)
    {
        var inv = _invites.FirstOrDefault(i => i.InviteId == inviteId && i.ToPlayerId == acceptorId);
        if (inv == null) return false;
        if (_playerClan.ContainsKey(acceptorId)) { _invites.Remove(inv); Touch(); return false; }

        var clan = GetClan(inv.ClanId);
        if (clan == null) { _invites.Remove(inv); Touch(); return false; }

        clan.MemberIds.Add(acceptorId);
        _playerClan[acceptorId] = clan.Id;
        _invites.Remove(inv);
        Touch();
        return true;
    }

    public bool DeclineInvite(string inviteId, string declinerId)
    {
        var inv = _invites.FirstOrDefault(i => i.InviteId == inviteId && i.ToPlayerId == declinerId);
        if (inv == null) return false;
        _invites.Remove(inv);
        Touch();
        return true;
    }

    public bool LeaveClan(string playerId)
    {
        if (!_playerClan.TryGetValue(playerId, out var clanId)) return false;
        var clan = GetClan(clanId);
        if (clan == null) return false;

        clan.MemberIds.Remove(playerId);
        _playerClan.Remove(playerId);

        // Sahip ayrýlýrsa: sahipliði sýradaki üyeye ver, kimse kalmazsa klaný sil
        if (clan.OwnerId == playerId)
        {
            if (clan.MemberIds.Count > 0) clan.OwnerId = clan.MemberIds[0];
            else _clans.Remove(clan);
        }
        Touch();
        return true;
    }
}
