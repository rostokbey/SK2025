using UnityEngine;

public class SeasonModeDialog : MonoBehaviour
{
    public GameObject panel;
    public MatchStarter matchStarter;

    private void Awake()
    {
        if (matchStarter == null)
            matchStarter = FindObjectOfType<MatchStarter>(true);
    }

    public void Open()
    {
        if (panel != null)
            panel.SetActive(true);
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    public void Play1v1() => StartSeasonMatch(1);
    public void Play2v2() => StartSeasonMatch(2);
    public void Play3v3() => StartSeasonMatch(3);

    private void StartSeasonMatch(int teamSize)
    {
        if (matchStarter == null)
        {
            Debug.LogError("[SeasonModeDialog] MatchStarter bulunamadı.");
            return;
        }

        matchStarter.StartMatch(teamSize, true);
        Close();
    }
}
