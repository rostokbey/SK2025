#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace SK2025.VFX.Editor
{
    public class VFXSetup
    {
        [MenuItem("Tools/SK2025/VFX Altyapisini Kur")]
        public static void Setup()
        {
            // Klasörleri oluştur
            Directory.CreateDirectory("Assets/_Project/VFX/Prefabs");
            Directory.CreateDirectory("Assets/_Project/VFX/Data");
            Directory.CreateDirectory("Assets/_Project/Scripts/VFX");

            // Paketleri kontrol et - manifest'e ekle
            var manifestPath = "Packages/manifest.json";
            var manifest = File.ReadAllText(manifestPath);
            if (!manifest.Contains("visualeffectgraph"))
            {
                Debug.Log("VFX Graph paketi ekleniyor... Lütfen Package Manager'dan Visual Effect Graph'i kurun: 14.0.9");
                UnityEditor.PackageManager.Client.Add("com.unity.visualeffectgraph@14.0.9");
            }

            // Manager Prefab oluştur
            var managerGo = new GameObject("VFXManager");
            managerGo.AddComponent<VFXManager>();
            PrefabUtility.SaveAsPrefabAsset(managerGo, "Assets/_Project/VFX/VFXManager.prefab");
            Object.DestroyImmediate(managerGo);

            Debug.Log("<color=green>[SK2025] VFX Altyapısı Hazır! Şimdi VFXManager prefabını ilk sahneye sürükle.</color>");
        }
    }
}
#endif