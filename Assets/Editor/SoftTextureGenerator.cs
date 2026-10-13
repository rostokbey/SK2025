using System.IO;
using UnityEditor;
using UnityEngine;

public static class SoftTextureGenerator
{
    [MenuItem("Tools/SK2025/Yumusak Partikul Dokusu Olustur")]
    public static void CreateSoftCircleTexture()
    {
        Texture2D texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);

        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float dx = (x - 63.5f) / 64f;
                float dy = (y - 63.5f) / 64f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha *= alpha;

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        string folder = Path.Combine(Application.dataPath, "Resources", "VFX");
        Directory.CreateDirectory(folder);

        string filePath = Path.Combine(folder, "SoftCircle.png");
        File.WriteAllBytes(filePath, texture.EncodeToPNG());

        Object.DestroyImmediate(texture);
        AssetDatabase.Refresh();

        Debug.Log("[SK2025] SoftCircle.png olusturuldu: " + filePath);
    }
}