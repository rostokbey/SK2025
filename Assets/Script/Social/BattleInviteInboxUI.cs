using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleInviteInboxUI : MonoBehaviour
{
    public RectTransform listParent;
    public GameObject rowPrefab; // Metin + Kabul + Reddet

    void OnEnable()
    {
        BattleInviteMock.Instance.OnChanged += Refresh;
        Refresh();
    }
    void OnDisable() => BattleInviteMock.Instance.OnChanged -= Refresh;

    void Refresh()
    {
        foreach (Transform t in listParent) Destroy(t.gameObject);

        var me = string.IsNullOrEmpty(SocialIdentity.ProfileId) ? "P1" : SocialIdentity.ProfileId;
        var inbox = BattleInviteMock.Instance.GetInbox(me);

        foreach (var inv in inbox)
        {
            var row = Instantiate(rowPrefab, listParent);
            row.GetComponentInChildren<TextMeshProUGUI>().text =
                $"Düello daveti: {inv.FromPlayerId} → {inv.ToPlayerId}";

            var btns = row.GetComponentsInChildren<Button>();
            btns[0].onClick.AddListener(() =>
            {
                BattleInviteMock.Instance.Accept(inv.InviteId, me, () =>
                {
                    Debug.Log("Düello kabul edildi → maça geç!");
                    // Buraya StartBattleManager tetik bağlayacaksın.
                });
            });
            btns[1].onClick.AddListener(() => BattleInviteMock.Instance.Decline(inv.InviteId, me));
        }
    }
}
