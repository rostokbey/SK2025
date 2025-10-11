using UnityEngine;
using TMPro;

public class LocalLogin : MonoBehaviour
{
    [Header("Opsiyonel UI")]
    public TMP_InputField usernameInput;

    // Profil seçildiðinde çaðýrmak için basit yardýmcý
    public void SelectProfileAndLoad()
    {
        string profileId = (usernameInput && !string.IsNullOrWhiteSpace(usernameInput.text))
            ? usernameInput.text.Trim()
            : "DEFAULT";

        TryLoadInventory(profileId);
    }

    public void TryLoadInventory(string profileId)
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null) { Debug.LogError("[LocalLogin] PlayerInventory bulunamadý."); return; }

        inv.LoadFromDisk(profileId);
        Debug.Log($"[LocalLogin] Envanter yüklendi (profil={profileId}).");
    }

    public void TrySaveInventory()
    {
        var inv = PlayerInventory.Instance ?? FindObjectOfType<PlayerInventory>();
        if (inv == null) { Debug.LogError("[LocalLogin] PlayerInventory bulunamadý."); return; }

        string pid = !string.IsNullOrEmpty(inv.CurrentProfileId) ? inv.CurrentProfileId : "DEFAULT";
        inv.SaveToDisk(pid);
        Debug.Log($"[LocalLogin] Envanter kaydedildi. (profil={pid})");
    }
}