using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// TEST paneli (yalnızca Editor ve Development Build'lerde, AccountService tarafından eklenir).
/// Sağ üstteki "Hesap" düğmesi paneli açar: durum, oyuncu kimliği, kullanıcı adı bağlama ve giriş.
/// IMGUI kullandığı için sahneye prefab/canvas eklemek gerekmez. Canlı sürümde yerine gerçek bir
/// giriş ekranı yapılır.
/// </summary>
public class AccountDebugPanel : MonoBehaviour
{
    private bool open;
    private bool working;
    private string username = string.Empty;
    private string password = string.Empty;
    private string status = string.Empty;

    private void OnGUI()
    {
        AccountService service = AccountService.Instance;
        if (service == null)
            return;

        Matrix4x4 previousMatrix = GUI.matrix;
        try
        {
            // Yüksek DPI'lı telefonlarda IMGUI çok küçük kalmasın.
            float scale = Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 160f : 1f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            if (!open)
            {
                if (GUI.Button(new Rect(width - 80f, 8f, 72f, 28f), "Hesap"))
                    open = true;
                return;
            }

            GUILayout.BeginArea(new Rect(width - 300f, 8f, 292f, 380f), GUI.skin.box);

            GUILayout.Label($"Durum: {service.State}");
            GUILayout.Label($"Oyuncu: {Shorten(service.PlayerId)}");
            GUILayout.Label($"Kullanıcı adı: {(service.HasLinkedUsername ? service.Username : "bağlı değil")}");
            if (!string.IsNullOrEmpty(service.LastError))
                GUILayout.Label($"Hata: {service.LastError}");

            GUI.enabled = !working;

            GUILayout.Label("Kullanıcı adı");
            username = GUILayout.TextField(username, 64);
            GUILayout.Label("Şifre");
            password = GUILayout.PasswordField(password, '*', 64);

            if (GUILayout.Button("Bu oyuncuya bağla"))
                Run(() => service.LinkUsernamePasswordAsync(username, password));

            if (GUILayout.Button("Mevcut hesaba giriş yap"))
                Run(() => service.SignInUsernamePasswordAsync(username, password));

            if (GUILayout.Button("Yeni test kimliği"))
                Run(() => service.CreateNewTestIdentityAsync());

            GUI.enabled = true;

            if (!string.IsNullOrEmpty(status))
                GUILayout.Label(status);

            if (GUILayout.Button("Kapat"))
                open = false;

            GUILayout.EndArea();
        }
        finally
        {
            GUI.matrix = previousMatrix;
        }
    }

    private async void Run(Func<Task<AccountService.AccountResult>> action)
    {
        working = true;
        status = "İşleniyor...";

        try
        {
            AccountService.AccountResult result = await action();
            status = result.Message;
        }
        catch (Exception e)
        {
            status = e.Message;
        }
        finally
        {
            working = false;
        }
    }

    private static string Shorten(string id)
    {
        if (string.IsNullOrEmpty(id)) return "-";
        return id.Length <= 10 ? id : id.Substring(0, 10) + "…";
    }
}
