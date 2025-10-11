using System;
using System.Collections.Generic;

[Serializable]
public class PlayerSummary
{
    public string PlayerId;
    public string DisplayName;

    public PlayerSummary(string id, string name)
    { PlayerId = id; DisplayName = name; }
}

[Serializable]
public class Clan
{
    public string Id;
    public string Name;
    public string OwnerId;
    public List<string> MemberIds = new();

    public bool IsMember(string playerId) => MemberIds.Contains(playerId);
}

[Serializable]
public class ClanInvite
{
    public string InviteId;
    public string ClanId;
    public string FromPlayerId;
    public string ToPlayerId;
    public DateTime CreatedAtUtc = DateTime.UtcNow;
}
