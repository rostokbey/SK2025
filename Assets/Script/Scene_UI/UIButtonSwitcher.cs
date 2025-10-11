using UnityEngine;
using System;

public class UIButtonSwitcher : MonoBehaviour
{
    public void SwitchPanelByName(string panelName)
    {
        if (Enum.TryParse(panelName, out SceneUIController.TargetPanel target))
        {
            FindObjectOfType<SceneUIController>()?.ShowOnly(target);
        }
        else
        {
            Debug.LogError("Geçersiz panel adý: " + panelName);
        }
    }
}
