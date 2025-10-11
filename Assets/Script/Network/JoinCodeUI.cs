using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class JoinCodeUI : MonoBehaviour
{
    [Header("Refs")]
    public GameObject panel;         // tüm kutu (kök)
    public TMP_Text codeText;        // "ABC123"
    public Button copyButton;        // "Kopyala"
    public Button closeButton;       // (opsiyonel) paneli gizle

    string _code = "";

    void Awake()
    {
        if (copyButton) copyButton.onClick.AddListener(CopyToClipboard);
        if (closeButton) closeButton.onClick.AddListener(Clear);
    }

    public void SetCode(string code)
    {
        _code = code ?? "";
        if (codeText) codeText.text = string.IsNullOrEmpty(_code) ? "-" : _code;
        if (panel) panel.SetActive(!string.IsNullOrEmpty(_code));
    }

    public void Clear()
    {
        _code = "";
        if (codeText) codeText.text = "-";
        if (panel) panel.SetActive(false);
    }

    public void CopyToClipboard()
    {
        if (string.IsNullOrEmpty(_code)) return;
        GUIUtility.systemCopyBuffer = _code;
        Debug.Log($"[Lobby] Join code copied: {_code}");
    }
}
