using UnityEngine;

public class ClanManager : MonoBehaviour
{
    public static ClanManager Instance { get; private set; }
    public ClanRepository repository;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Statik SocialIdentity'den çek
    string MeId => string.IsNullOrEmpty(SocialIdentity.ProfileId) ? "P1" : SocialIdentity.ProfileId;
    string MeName => string.IsNullOrEmpty(SocialIdentity.PlayerName) ? "Me" : SocialIdentity.PlayerName;

    public bool CreateClan(string name) => repository.TryCreateClan(name, MeId, out _);
    public bool InviteFriend(string friendId)
    {
        var myClan = repository.GetMyClanId(MeId);
        return repository.SendInvite(myClan, MeId, friendId);
    }
    public bool Accept(string inviteId) => repository.AcceptInvite(inviteId, MeId);
    public bool Decline(string inviteId) => repository.DeclineInvite(inviteId, MeId);
    public bool LeaveMyClan() => repository.LeaveClan(MeId);
}
