using UnityEngine;

public class SeasonModeDialog : MonoBehaviour
{
    public GameObject panel;
    public BattleStartAdapter adapter; // Inspector’dan Managers’taki adapter’ý sürükle

    public void Open() { if (panel) panel.SetActive(true); }
    public void Close() { if (panel) panel.SetActive(false); }

    public void Play1v1() { adapter.StartSeason(1); Close(); }
    public void Play2v2() { adapter.StartSeason(2); Close(); }
    public void Play3v3() { adapter.StartSeason(3); Close(); }
}
