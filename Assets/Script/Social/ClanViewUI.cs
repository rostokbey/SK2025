using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClanViewUI : MonoBehaviour
{
    public static ClanViewUI Instance;

    [Header("UI References")]
    [SerializeField] private TMP_InputField createClanNameInput;
    [SerializeField] private Button createClanButton;

    [SerializeField] private TMP_Dropdown inviteFriendDropdown;
    [SerializeField] private Button sendInviteButton;

    [SerializeField] private RectTransform invitesListParent;
    [SerializeField] private GameObject inviteRowPrefab; // Prefab: metin + Kabul + Reddet

    [SerializeField] private Button leaveClanButton;
    [SerializeField] private TextMeshProUGUI myClanLabel;

    [SerializeField] private RectTransform membersListParent;
    [SerializeField] private GameObject memberRowPrefab; // Prefab: sadece isim label

    [Header("Refs")]
    [SerializeField] private ClanManager manager;
    [SerializeField] private ClanRepository repo;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        if (repo != null)
            repo.OnDataChanged += RefreshAll;
        RefreshAll();
    }

    private void OnDisable()
    {
        if (repo != null)
            repo.OnDataChanged -= RefreshAll;
    }

    private void Start()
    {
        if (createClanButton != null)
            createClanButton.onClick.AddListener(() =>
            {
                if (manager.CreateClan(createClanNameInput.text))
                    createClanNameInput.text = "";
            });

        if (sendInviteButton != null)
            sendInviteButton.onClick.AddListener(() =>
            {
                if (inviteFriendDropdown.options.Count == 0) return;
                var toId = inviteFriendDropdown.options[inviteFriendDropdown.value].text;
                manager.InviteFriend(toId);
            });

        if (leaveClanButton != null)
            leaveClanButton.onClick.AddListener(() => manager.LeaveMyClan());
    }

    private void RefreshAll()
    {
        if (repo == null || manager == null) return;

        var me = string.IsNullOrEmpty(SocialIdentity.ProfileId) ? "P1" : SocialIdentity.ProfileId;
        var myClanId = repo.GetMyClanId(me);
        var myClan = string.IsNullOrEmpty(myClanId) ? null : repo.GetClan(myClanId);

        // Etiket
        if (myClanLabel != null)
            myClanLabel.text = myClan != null ? $"Klanım: {myClan.Name}" : "Klanda değilim";

        // Üyeler
        foreach (Transform t in membersListParent) Destroy(t.gameObject);
        if (myClan != null)
        {
            foreach (var pid in myClan.MemberIds)
            {
                var go = Instantiate(memberRowPrefab, membersListParent);
                go.GetComponentInChildren<TextMeshProUGUI>().text = pid;
            }
        }

        // Davetler
        foreach (Transform t in invitesListParent) Destroy(t.gameObject);
        var invites = repo.GetInvitesFor(me);
        foreach (var inv in invites)
        {
            var row = Instantiate(inviteRowPrefab, invitesListParent);
            var labels = row.GetComponentsInChildren<TextMeshProUGUI>();
            labels[0].text = $"Klan daveti → {repo.GetClan(inv.ClanId)?.Name} (from {inv.FromPlayerId})";

            var btns = row.GetComponentsInChildren<Button>();
            btns[0].onClick.AddListener(() => manager.Accept(inv.InviteId));
            btns[1].onClick.AddListener(() => manager.Decline(inv.InviteId));
        }

        // Arkadaş listesi (mock veya gerçek repository’den)
        var friends = GetMockFriends().Where(f => f.PlayerId != me).ToList();
        inviteFriendDropdown.ClearOptions();
        inviteFriendDropdown.AddOptions(friends.Select(f => new TMP_Dropdown.OptionData(f.PlayerId)).ToList());
    }

    // === Statik Show / Hide ===
    public static void Show()
    {
        if (Instance == null)
        {
            Debug.LogWarning("ClanViewUI sahnede yok!");
            return;
        }
        Instance.gameObject.SetActive(true);
    }

    public static void Hide()
    {
        if (Instance == null) return;
        Instance.gameObject.SetActive(false);
    }

    // Mock friend listesi
    private static PlayerSummary[] GetMockFriends() => new[]
    {
        new PlayerSummary("P1","Sen"),
        new PlayerSummary("P2","Arkadaş-2"),
        new PlayerSummary("P3","Arkadaş-3"),
        new PlayerSummary("P4","Arkadaş-4"),
    };
}
