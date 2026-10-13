using UnityEngine;
using UnityEditor;
using System.IO;

public class FixVFXShaders
{
    [MenuItem("Tools/Fix VFX Shaders to URP")]
    public static void FixAllVFXShaders()
    {
        // VFX klasörlerindeki tüm materialları bul
        string[] vfxFolders = new[] { "Assets/Vefects", "Assets/_VFX", "Assets/VFX", "Assets/Resources/VFX" };
        int fixedCount = 0;

        foreach (string folder in vfxFolders)
        {
            if (!Directory.Exists(folder)) continue;

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null) continue;

                Shader current = mat.shader;
                bool needsFix = false;

                // Shader null, missing veya URP değilse düzelt
                if (current == null ||
                    current.name.Contains("Hidden/InternalErrorShader") ||
                    current.name.Contains("Particles/Standard") ||
                    current.name == "Standard" ||
                    !current.name.Contains("Universal Render Pipeline"))
                {
                    needsFix = true;
                }

                if (needsFix)
                {
                    // VFX için en uygun shader
                    Shader newShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

                    // Yoksa basit lit dene
                    if (newShader == null)
                        newShader = Shader.Find("Universal Render Pipeline/Simple Lit");

                    // O da yoksa Lit
                    if (newShader == null)
                        newShader = Shader.Find("Universal Render Pipeline/Lit");

                    if (newShader != null)
                    {
                        mat.shader = newShader;
                        EditorUtility.SetDirty(mat);
                        fixedCount++;
                        Debug.Log($"<color=green>Düzeltildi:</color> {path} → {newShader.name}");
                    }
                    else
                    {
                        Debug.LogError($"<color=red>URP Shader bulunamadı!</color> Önce URP paketini kurmalısın.");
                        return;
                    }
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=green>✅ Toplam {fixedCount} material düzeltildi!</color> Şimdi VFX'leri test et.");
        EditorUtility.DisplayDialog("Tamamlandı", $"{fixedCount} adet VFX material URP'ye çevrildi. Şimdi VFX'leri kontrol et.", "Tamam");
    }
}