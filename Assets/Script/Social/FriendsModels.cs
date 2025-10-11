using System;
using System.Collections.Generic;

[Serializable]
public class FriendEntry
{
    public string displayName;     // arkadaþýn görünen adý
    public string status;          // "Friend", "PendingOut", "PendingIn"
}

[Serializable]
public class FriendInvite
{
    public string fromName;
    public string toName;
    public long createdUnix;
}

[Serializable]
public class FriendsTable
{
    public List<FriendEntry> friends = new List<FriendEntry>();
    public List<FriendInvite> inbox = new List<FriendInvite>();   // bana gelen davetler
    public List<FriendInvite> outbox = new List<FriendInvite>();  // benim attýklarým
}
