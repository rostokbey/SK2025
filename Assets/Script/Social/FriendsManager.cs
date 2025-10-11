using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FriendsManager : MonoBehaviour
{
    public static FriendsManager Instance { get; private set; }

    FriendsTable table;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        table = FriendsRepository.Load();
        SeedIfEmpty();
    }

    void SeedIfEmpty()
    {
        if (table.friends.Count == 0 && table.inbox.Count == 0 && table.outbox.Count == 0)
        {
            // Demo için gelen 2 davet
            table.inbox.Add(new FriendInvite { fromName = "Ece", toName = SocialIdentity.PlayerName, createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            table.inbox.Add(new FriendInvite { fromName = "Bora", toName = SocialIdentity.PlayerName, createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            FriendsRepository.Save(table);
        }
    }

    public IReadOnlyList<FriendEntry> GetFriends() =>
        table.friends.Where(f => f.status == "Friend").OrderBy(f => f.displayName).ToList();

    public IReadOnlyList<FriendInvite> GetInbox() => table.inbox;
    public IReadOnlyList<FriendInvite> GetOutbox() => table.outbox;

    public void SendInvite(string toName)
    {
        if (string.IsNullOrWhiteSpace(toName)) return;
        if (toName == SocialIdentity.PlayerName) return;

        // zaten arkadaþ mý?
        if (table.friends.Any(f => f.displayName.Equals(toName, StringComparison.OrdinalIgnoreCase)))
            return;

        // outbox'a ekle
        table.outbox.Add(new FriendInvite
        {
            fromName = SocialIdentity.PlayerName,
            toName = toName,
            createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        FriendsRepository.Save(table);
        Debug.Log($"[Friends] Invite sent to {toName}");

        // Offline mock: Ýstersen karþýdan "geri dönüþ" simüle etme
        // Burada elle bir þey yapmýyoruz; iskelet yeterli.
    }

    public void AcceptInvite(int inboxIndex)
    {
        if (inboxIndex < 0 || inboxIndex >= table.inbox.Count) return;
        var inv = table.inbox[inboxIndex];

        // arkadaþ listesine ekle
        table.friends.Add(new FriendEntry { displayName = inv.fromName, status = "Friend" });

        // inbox'tan kaldýr
        table.inbox.RemoveAt(inboxIndex);

        FriendsRepository.Save(table);
        Debug.Log($"[Friends] Accepted invite from {inv.fromName}");
    }

    public void DeclineInvite(int inboxIndex)
    {
        if (inboxIndex < 0 || inboxIndex >= table.inbox.Count) return;
        var inv = table.inbox[inboxIndex];
        table.inbox.RemoveAt(inboxIndex);
        FriendsRepository.Save(table);
        Debug.Log($"[Friends] Declined invite from {inv.fromName}");
    }

    public void RemoveFriend(string name)
    {
        var idx = table.friends.FindIndex(f => f.displayName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            table.friends.RemoveAt(idx);
            FriendsRepository.Save(table);
            Debug.Log($"[Friends] Removed {name}");
        }
    }
}
