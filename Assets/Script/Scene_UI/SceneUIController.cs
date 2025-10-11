using UnityEngine;
using System.Collections;

public class SceneUIController : MonoBehaviour
{
    public enum TargetPanel { Login, Lobby, QR, DeckBuilder, DeckSelect, Season, Friends, Clan, Anamenu }

    [Header("Panels")]
    public GameObject loginPanel;
    public GameObject lobbyPanel;
    public GameObject qrPanel;
    public GameObject deckBuilderPanel;
    public GameObject deckSelectPanel;
    public GameObject seasonPanel;
    public GameObject friendsPanel;
    public GameObject clanPanel;
    public GameObject anamenuPanel;

    [Header("Global UI")]
    public GameObject bottomNav;
    public GameObject backToMenuButton;

    [Header("Startup")]
    public TargetPanel startPanel = TargetPanel.Login;

    private int _modalDepth = 0;

    void Start()
    {
        InitializeAllPanels();
        ShowOnly(startPanel);
    }

    private void InitializeAllPanels()
    {
        SafeSetActive(loginPanel, false);
        SafeSetActive(lobbyPanel, false);
        SafeSetActive(qrPanel, false);
        SafeSetActive(deckBuilderPanel, false);
        SafeSetActive(deckSelectPanel, false);
        SafeSetActive(seasonPanel, false);
        SafeSetActive(friendsPanel, false);
        SafeSetActive(clanPanel, false);
        SafeSetActive(anamenuPanel, false);

        // BottomNav sadece Anamenu'de gözükecek
        SafeSetActive(bottomNav, false);
        SafeSetActive(backToMenuButton, false);
    }

    public void ShowOnly(TargetPanel target)
    {
        // Önce tüm panelleri kapat
        CloseAllPanels();

        // Hedef paneli aç
        switch (target)
        {
            case TargetPanel.Login:
                SafeSetActive(loginPanel, true);
                break;
            case TargetPanel.Lobby:
                SafeSetActive(lobbyPanel, true);
                break;
            case TargetPanel.QR:
                SafeSetActive(qrPanel, true);
                break;
            case TargetPanel.DeckBuilder:
                SafeSetActive(deckBuilderPanel, true);
                break;
            case TargetPanel.DeckSelect:
                SafeSetActive(deckSelectPanel, true);
                break;
            case TargetPanel.Season:
                SafeSetActive(seasonPanel, true);
                break;
            case TargetPanel.Friends:
                SafeSetActive(friendsPanel, true);
                break;
            case TargetPanel.Clan:
                SafeSetActive(clanPanel, true);
                break;
            case TargetPanel.Anamenu:
                SafeSetActive(anamenuPanel, true);
                break;
        }

        UpdateGlobalUI(target);
    }

    private void CloseAllPanels()
    {
        SafeSetActive(loginPanel, false);
        SafeSetActive(lobbyPanel, false);
        SafeSetActive(qrPanel, false);
        SafeSetActive(deckBuilderPanel, false);
        SafeSetActive(deckSelectPanel, false);
        SafeSetActive(seasonPanel, false);
        SafeSetActive(friendsPanel, false);
        SafeSetActive(clanPanel, false);
        SafeSetActive(anamenuPanel, false);
    }

    private void UpdateGlobalUI(TargetPanel target)
    {
        bool isAnamenu = target == TargetPanel.Anamenu;
        bool shouldShowBottomNav = isAnamenu && _modalDepth == 0;
        bool shouldShowBackButton = target == TargetPanel.Season && _modalDepth == 0;

        SafeSetActive(bottomNav, shouldShowBottomNav);
        SafeSetActive(backToMenuButton, shouldShowBackButton);
    }

    private void SafeSetActive(GameObject gameObject, bool active)
    {
        if (gameObject != null)
        {
            gameObject.SetActive(active);
        }
    }

    public void SetModal(bool isModal)
    {
        _modalDepth += isModal ? 1 : -1;
        if (_modalDepth < 0) _modalDepth = 0;

        // Mevcut paneli yenile
        TargetPanel currentPanel = GetCurrentPanel();
        UpdateGlobalUI(currentPanel);

        Debug.Log($"Modal Depth: {_modalDepth}");
    }

    private TargetPanel GetCurrentPanel()
    {
        if (IsPanelActive(loginPanel)) return TargetPanel.Login;
        if (IsPanelActive(lobbyPanel)) return TargetPanel.Lobby;
        if (IsPanelActive(qrPanel)) return TargetPanel.QR;
        if (IsPanelActive(deckBuilderPanel)) return TargetPanel.DeckBuilder;
        if (IsPanelActive(deckSelectPanel)) return TargetPanel.DeckSelect;
        if (IsPanelActive(seasonPanel)) return TargetPanel.Season;
        if (IsPanelActive(friendsPanel)) return TargetPanel.Friends;
        if (IsPanelActive(clanPanel)) return TargetPanel.Clan;
        if (IsPanelActive(anamenuPanel)) return TargetPanel.Anamenu;

        return TargetPanel.Login;
    }

    private bool IsPanelActive(GameObject panel)
    {
        return panel != null && panel.activeSelf;
    }

    // Navigation Methods
    public void GoToLogin() => ShowOnly(TargetPanel.Login);
    public void GoToLobby() => ShowOnly(TargetPanel.Lobby);
    public void GoToQR() => ShowOnly(TargetPanel.QR);
    public void GoToDeckBuilder() => ShowOnly(TargetPanel.DeckBuilder);
    public void GoToDeckSelect() => ShowOnly(TargetPanel.DeckSelect);
    public void GoToSeason() => ShowOnly(TargetPanel.Season);
    public void GoToFriends() => ShowOnly(TargetPanel.Friends);
    public void GoToClan() => ShowOnly(TargetPanel.Clan);
    public void GoToAnamenu() => ShowOnly(TargetPanel.Anamenu);
}