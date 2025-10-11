using System;

[Serializable]
public class BattleInvite
{
    public string InviteId;
    public string FromPlayerId;
    public string ToPlayerId;
    public DateTime CreatedAtUtc = DateTime.UtcNow;
}
